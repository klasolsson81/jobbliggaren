using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCriterionAds;

/// <summary>
/// #1559 — loads the user's criterion (owner-scoped, ADR 0031 probe via
/// <see cref="CriterionOwnerScopedLoader"/>), asks the port which ACTIVE ads its companies posted,
/// then loads and projects those ads through <see cref="IAppDbContext"/>.
///
/// <para>
/// <b>Two round-trips, and the split is the firewall.</b> The port answers with ad IDS because the
/// JOIN is the only half that needs <c>company_register</c> — which is not a <c>DbSet</c> on
/// <see cref="IAppDbContext"/> (DPIA C-D4 / M-C5) and therefore unreachable from here. Projecting the
/// ad columns inside the register's Infrastructure file instead would give the job-ad read shape a
/// second home beside the Application-side projection every other ad surface uses. The precedent is
/// <c>ListNewFollowedCompanyAdsQueryHandler</c> (#1576), which projects <see cref="JobAdDto"/> the
/// same way over the same aggregate.
/// </para>
///
/// <para>
/// <b>The re-order is not redundant.</b> <c>WHERE id = ANY(...)</c> does not preserve the array's
/// order, so the second query re-states the port's published order — <c>PublishedAt</c> descending,
/// then <c>Id</c> — rather than trusting the ids to arrive sorted. Both orders are TOTAL and equal by
/// construction; a page ordered differently from the one it was paginated against would drop and
/// duplicate rows across pages, which is the failure the port appends its PK to avoid.
/// </para>
///
/// <para>
/// <b>No org.nr crosses this handler.</b> The port returns ad ids and <see cref="JobAdDto"/> carries
/// no organisation number, so the personnummer guard (ADR 0087 D8(c)) has nothing to mask here — the
/// raw org.nr of a matched sole trader stays server-side inside the join, exactly as it does for the
/// counts. The ads themselves are public Platsbanken data.
/// </para>
/// </summary>
public sealed class BrowseCriterionAdsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse)
    : IQueryHandler<BrowseCriterionAdsQuery, PagedResult<JobAdDto>?>
{
    public async ValueTask<PagedResult<JobAdDto>?> Handle(
        BrowseCriterionAdsQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, nameof(BrowseCriterionAdsQuery), cancellationToken);

        if (criterion is null)
            return null;

        var page = await browse.BrowseAdIdsAsync(
            new CompanyBrowseCriteria(criterion.Criteria, query.Page, query.PageSize),
            cancellationToken);

        // An empty page short-circuits: `= ANY('{}')` would be a round-trip that cannot match a row.
        if (page.Items.Count == 0)
            return new PagedResult<JobAdDto>([], page.TotalCount, page.Page, page.PageSize);

        var ids = page.Items.Select(id => new JobAdId(id)).ToList();

        var items = await db.JobAds
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id))
            .OrderByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id)
            .Select(j => new JobAdDto(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.Url,
                j.Source.Value,
                j.Status.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, page.TotalCount, page.Page, page.PageSize);
    }
}
