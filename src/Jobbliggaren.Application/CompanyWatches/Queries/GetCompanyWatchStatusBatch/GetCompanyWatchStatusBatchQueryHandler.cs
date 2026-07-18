using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;

/// <summary>
/// #311 #455 (ADR 0087 D8(c)) — composes the follow-state overlay for a page of ads. Correlates the
/// current user's active follows (bounded — a handful) against each requested ad's employer org.nr
/// (resolved server-side via <see cref="IJobAdEmployerReader"/>). The raw org.nr is used ONLY to
/// correlate and is NEVER placed in the returned <see cref="CompanyWatchStatusDto"/> — the FE receives
/// the opaque <c>CompanyWatchId</c> (for unfollow) and a <c>Followable</c> flag, nothing more.
///
/// <para>
/// <b>Three correlation channels (target- and token-aware):</b> a page ad's PLAINTEXT org.nr (from
/// public <c>job_ads</c>) is matched against (1) EMPLOYER watches with an AB org.nr (direct plaintext),
/// (2) EMPLOYER watches of a personnummer-shaped (enskild-firma) org.nr — stored HMAC-tokenised at rest
/// (#544, ADR 0090 D5), so the ad's plaintext is tokenised and probed (plus a legacy raw probe for the
/// backfill window; the same dual-probe as the scan/executor), and (3) BRAND_GROUP watches whose curated
/// member list includes the ad's org.nr (#311 PR-5, ADR 0087 D4). <b>Precedence: a DIRECT employer watch
/// wins over a group watch</b> — the returned id feeds <c>DELETE /{id}</c> (surrogate-id unfollow), and
/// returning the group id would make an ad-card unfollow silently delete a whole brand-group follow.
/// </para>
/// </summary>
public sealed class GetCompanyWatchStatusBatchQueryHandler(
    IAppDbContext db,
    IJobAdEmployerReader employerReader,
    ICurrentUser currentUser,
    IProtectedIdentityTokenizer tokenizer,
    IBrandGroupProvider brandGroups)
    : IQueryHandler<GetCompanyWatchStatusBatchQuery, CompanyWatchStatusBatchDto>
{
    private static readonly CompanyWatchStatusBatchDto Empty = new([]);

    public async ValueTask<CompanyWatchStatusBatchDto> Handle(
        GetCompanyWatchStatusBatchQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue || query.JobAdIds.Count == 0)
            return Empty;

        var userId = currentUser.UserId.Value;

        // The user's active follows. Load the (bounded) entities then correlate client-side — a
        // value-converted VO member does not project cleanly server-side.
        var watches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
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

        // org.nr per requested ad, resolved server-side (raw org.nr stays here, never surfaced/logged).
        var orgNrByJobAd = await employerReader.GetOrganizationNumbersByJobAdIdsAsync(
            query.JobAdIds, cancellationToken);

        var statuses = query.JobAdIds
            .Distinct()
            .Select(jobAdId =>
            {
                var followable = orgNrByJobAd.TryGetValue(jobAdId, out var orgNr) && orgNr is not null;
                Guid? companyWatchId = followable ? Correlate(orgNr!) : null;
                return new CompanyWatchStatusDto(jobAdId, companyWatchId, followable);
            })
            .ToList();

        return new CompanyWatchStatusBatchDto(statuses);

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
