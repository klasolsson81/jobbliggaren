using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// ADR 0175 D6 (#1663) — the one definition of a Branschbevakning's hits that the scan, the count, the page of new
/// ads and (in PR 3) the bell share. A sibling of <c>NewFollowedCompanyAdSet</c>, never joined to it: that set is
/// keyed on follows and answers nothing for a user who follows no company.
/// </summary>
internal static class NewCriterionAdSet
{
    /// <summary>
    /// The user's hits whose ad is no longer Active: the scan's retention purge (ADR 0175 D10), which runs for every
    /// due user before anything else and writes no flag. Active is an allow-list (<c>NOT EXISTS</c> an Active ad
    /// with the hit's id), so an Archived or Erased ad, and an ad row that is gone, are all purged; an ad never
    /// returns to Active, so nothing a surface shows is removed.
    ///
    /// <para>
    /// The <c>JobAds</c> read sits at statement level on purpose: written inside the lambda it compiles to an
    /// expression tree, which the lifecycle registry's IL scan cannot see. The caller deletes with
    /// <c>ExecuteDeleteAsync</c> on this query, whose element type is the hit, so no ad can be deleted through it.
    /// </para>
    /// </summary>
    internal static IQueryable<CompanyWatchCriterionAdHit> HitsWhoseAdLeftActive(IAppDbContext db, Guid userId)
    {
        var activeAds = db.JobAds.AsNoTracking().Where(j => j.Status == JobAdStatus.Active);
        return db.CompanyWatchCriterionAdHits
            .Where(h => h.UserId == userId && !activeAds.Any(j => j.Id == h.JobAdId));
    }
}
