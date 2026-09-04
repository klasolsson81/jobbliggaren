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
/// second home beside the Application-side projection every other ad surface uses.
/// </para>
///
/// <para>
/// <b>The page is re-sequenced by the port's ORDINAL, never by a re-derived key.</b>
/// <c>WHERE id = ANY(...)</c> does not preserve the array's order, so the loaded rows must be put
/// back in the order the port published — and the only faithful way is to follow
/// <c>page.Items</c> itself. Every attempt to re-derive it produces a DIFFERENT order: it cannot be
/// expressed in the query at all (<c>ThenBy(j =&gt; j.Id)</c> throws in-process, <c>j.Id.Value</c>
/// 500s against Postgres, <c>EF.Property&lt;Guid&gt;</c> binds the column on one provider and the CLR
/// property on the other), and sorting in memory silently introduces a THIRD one — Postgres compares
/// <c>uuid</c> bytewise while <c>Guid.CompareTo</c> reads the first field as a signed <c>Int32</c>,
/// so the two disagree on about half of all pairs.
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
            query.CriterionId, CriterionReadOperation.BrowseCriterionAds, cancellationToken);

        if (criterion is null)
            return null;

        var page = await browse.BrowseAdIdsAsync(
            new CompanyBrowseCriteria(criterion.Criteria, query.Page, query.PageSize),
            cancellationToken);

        // An empty page short-circuits: `= ANY('{}')` would be a round-trip that cannot match a row.
        if (page.Items.Count == 0)
            return new PagedResult<JobAdDto>([], page.TotalCount, page.Page, page.PageSize);

        var rows = await db.JobAds
            .AsNoTracking()
            .Where(j => page.Items.Contains(j.Id))
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

        // Follow the port's sequence. A row the port named and this load did not return has been
        // deleted between the two round-trips; it drops out here explicitly rather than shifting
        // everything after it.
        var byId = rows.ToDictionary(d => d.Id);
        var items = page.Items
            .Select(id => byId.GetValueOrDefault(id.Value))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();

        return new PagedResult<JobAdDto>(items, page.TotalCount, page.Page, page.PageSize);
    }
}
