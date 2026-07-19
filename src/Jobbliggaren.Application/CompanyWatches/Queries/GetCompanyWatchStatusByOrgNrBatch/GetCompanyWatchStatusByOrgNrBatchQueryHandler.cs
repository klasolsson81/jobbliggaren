using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;

/// <summary>
/// #560 company-search wave PR-C (CTO F3, ADR 0087 D8(c)) — the org.nr-keyed follow-state resolver for the
/// <c>/foretag/sok</c> results. Correlates the current user's active follows (bounded — a handful) against
/// each requested company's org.nr, which arrives PLAINTEXT in the request body (the register-search row
/// already carried it). Reads ONLY <c>company_watches</c> (owner-scoped) — never <c>company_register</c>
/// (the DPIA C-D4/M-C5 firewall keeps the register off <c>IAppDbContext</c>). The raw org.nrs are used
/// ONLY to correlate and are NEVER surfaced (the DTO carries opaque ids, no org.nr member) nor logged
/// (this file is on <c>RawOrgNrReadingSourcePaths</c>).
///
/// <para>
/// <b>Three correlation channels</b> (identical to the jobAdId-keyed sibling, minus the job-ad hop): the
/// request's plaintext org.nr is matched against (1) EMPLOYER watches with an AB org.nr (direct plaintext),
/// (2) EMPLOYER watches of a personnummer-shaped (enskild-firma) org.nr — stored HMAC-tokenised at rest
/// (#544, ADR 0090 D5), so the plaintext is tokenised and probed (plus a legacy raw probe for the backfill
/// window), and (3) BRAND_GROUP watches whose curated member list includes the org.nr (#311 PR-5). A DIRECT
/// employer watch wins over a group watch (the id feeds DELETE-by-id unfollow; the group id would make a
/// single-row unfollow silently delete a whole brand-group follow).
/// </para>
/// </summary>
public sealed class GetCompanyWatchStatusByOrgNrBatchQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProtectedIdentityTokenizer tokenizer,
    IBrandGroupProvider brandGroups)
    : IQueryHandler<GetCompanyWatchStatusByOrgNrBatchQuery, CompanyWatchStatusByOrgNrBatchDto>
{
    private static readonly CompanyWatchStatusByOrgNrBatchDto Empty = new([]);

    public async ValueTask<CompanyWatchStatusByOrgNrBatchDto> Handle(
        GetCompanyWatchStatusByOrgNrBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.OrganizationNumbers.Count == 0)
            return Empty;

        var userId = currentUser.UserId.Value;

        // The user's active follows. Load the (bounded) entities then correlate client-side — a
        // value-converted VO member does not project cleanly server-side. Ordered by CreatedAt so the
        // "first group wins" tie-break below (a member org.nr shared by two followed groups) is
        // DETERMINISTIC — the oldest follow wins, never DB row order (§2.3 predictable result).
        var watches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        // Three correlation maps (see class summary). The active-partial UNIQUEs give ≤1 active watch per
        // (user, org.nr) and per (user, brand_group_id), so each map value is well-defined.
        var employerPlaintextToId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var enskildKeyToId = new Dictionary<string, Guid>(StringComparer.Ordinal); // token or legacy raw pnr
        var memberOrgNrToGroupId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var w in watches)
        {
            if (w.TargetType == CompanyWatchTargetType.BrandGroup)
            {
                var group = w.BrandGroupId is null ? null : brandGroups.Catalog.Find(w.BrandGroupId.Value);
                if (group is null)
                    continue; // orphaned slug — matches nothing, never throws
                foreach (var member in group.MemberOrgNrs)
                    memberOrgNrToGroupId.TryAdd(member, w.Id.Value); // first group wins for a shared member
                continue;
            }

            if (w.OrganizationNumber is null)
                continue;
            if (w.OrganizationNumber.IsPersonnummerShaped())
                enskildKeyToId[w.OrganizationNumber.Value] = w.Id.Value;
            else
                employerPlaintextToId[w.OrganizationNumber.Value] = w.Id.Value;
        }

        // POSITIONAL projection: one status per requested org.nr, IN ORDER, NO .Distinct(). The FE zips the
        // response to its request list by index (see CompanyWatchStatusByOrgNrBatchDto) — deduping or
        // reordering here would misalign that zip. There is no Followable flag: the FE only ever sends
        // unmasked org.nrs from non-protected rows, so every entry is followable by construction.
        var statuses = query.OrganizationNumbers
            .Select(orgNr => new OrgNrFollowStatusDto(Correlate(orgNr)))
            .ToList();

        return new CompanyWatchStatusByOrgNrBatchDto(statuses);

        // DIRECT employer wins (AB plaintext, then enskild token/legacy probe), then brand-group member.
        Guid? Correlate(string orgNr)
        {
            if (employerPlaintextToId.TryGetValue(orgNr, out var abId))
                return abId;
            if (enskildKeyToId.Count > 0
                && (enskildKeyToId.TryGetValue(tokenizer.Tokenize(orgNr), out var tokenId)
                    || enskildKeyToId.TryGetValue(orgNr, out tokenId)))
                return tokenId;
            if (memberOrgNrToGroupId.TryGetValue(orgNr, out var groupId))
                return groupId;
            return null;
        }
    }
}
