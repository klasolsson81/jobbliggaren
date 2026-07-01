using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;

/// <summary>
/// ADR 0087 D3 — list the current user's active company follows. UserId-scoped (the soft-delete
/// query filter hides unfollowed rows). <c>company_name</c> is resolved at READ via a projection
/// over <c>job_ads</c> (ADR 0087 D2 — never a denormalised snapshot): the most-recent ad's employer
/// name per org.nr. The personnummer guard (FORK C1 / D8(c)) is applied per row in the projection —
/// a personnummer-shaped org.nr is masked (null) and flagged; the raw value never leaves this
/// handler un-flagged.
/// </summary>
public sealed class ListCompanyWatchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<ListCompanyWatchesQuery, IReadOnlyList<CompanyWatchDto>>
{
    public async ValueTask<IReadOnlyList<CompanyWatchDto>> Handle(
        ListCompanyWatchesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var userId = currentUser.UserId.Value;

        var watches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        if (watches.Count == 0)
            return [];

        // ADR 0087 D2 — resolve company_name at read from public job_ads. org.nr is a STORED
        // generated column exposed as the EF shadow property "OrganizationNumber" (parity with
        // JobAdSearchComposition, PR-2). The employer name per org.nr is enough to identify the watch
        // (display projection, not an invariant — graceful null when no current ad carries it). The
        // name is stable per org.nr (one legal entity = one name), so SELECT DISTINCT (org.nr, name)
        // is pushed server-side — the fetch is bounded to distinct pairs (a handful), never the full
        // ad set for a prolific employer (avoids the §5 unpaginated-fetch smell). string? element type
        // so the EF.Property<string?> Contains translates cleanly (the column is nullable; a NULL
        // org.nr ad never matches via `= ANY(...)`). Values themselves non-null.
        var orgNrs = watches.Select(w => (string?)w.OrganizationNumber.Value).Distinct().ToList();

        var nameByOrgNr = (await db.JobAds
                .AsNoTracking()
                .Where(j => orgNrs.Contains(EF.Property<string?>(j, "OrganizationNumber")))
                .Select(j => new { OrgNr = EF.Property<string?>(j, "OrganizationNumber"), Name = j.Company.Name })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Where(x => x.OrgNr is not null)
            .GroupBy(x => x.OrgNr!)
            .ToDictionary(g => g.Key, g => g.First().Name);

        return watches
            .Select(w =>
            {
                var isProtected = w.OrganizationNumber.IsPersonnummerShaped();
                return new CompanyWatchDto(
                    Id: w.Id.Value,
                    // FORK C1 / D8(c): never surface a personnummer-shaped org.nr.
                    OrganizationNumber: isProtected ? null : w.OrganizationNumber.Value,
                    IsProtectedIdentity: isProtected,
                    CompanyName: nameByOrgNr.GetValueOrDefault(w.OrganizationNumber.Value),
                    FollowedAt: w.CreatedAt);
            })
            .ToList();
    }
}
