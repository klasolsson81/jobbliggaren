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
    /// The user's NEW hits: a hit of a criterion whose last scan answered
    /// <see cref="CompanyWatchCriterionScanOutcome.Scanned"/>, written after both the criterion's last change and
    /// what the user has acknowledged on its page, for an ad that is still Active. Owner-scoped twice: on the hit's
    /// user and on its criterion's (ADR 0175 D7).
    ///
    /// <para>
    /// <b>Every surface that shows a number or a row reads this, and only criteria that answered Scanned appear in
    /// it</b>, so a TooBroad or never-materialised criterion shows neither a zero nor a stale count (ADR 0120). The
    /// lower bound <c>max(NewAdsSeenThrough, CriteriaChangedAt)</c> is written as two strict comparisons; it also
    /// keeps hits from a membership an edit replaced from surfacing as the edited criterion's (Art. 5(1)(d)). It is
    /// never <c>LastSeenFollowedAdsAt</c>, which would swallow these hits. The bell's flags are not read here: a
    /// hidden row's marks are kept (ADR 0168 D3).
    /// </para>
    ///
    /// <para>
    /// Returns the hit ENTITY, composed by its callers with their own join and projection: a projection composed
    /// after a constructor projection fails at run time in EF's shaper. The <c>JobAds</c> join is at statement
    /// level, where the lifecycle registry's IL scan sees it.
    /// </para>
    /// </summary>
    internal static IQueryable<CompanyWatchCriterionAdHit> NewHits(IAppDbContext db, Guid userId) =>
        from h in db.CompanyWatchCriterionAdHits.AsNoTracking()
        where h.UserId == userId
        join c in db.CompanyWatchCriteria.AsNoTracking() on h.CompanyWatchCriterionId equals c.Id
        where c.UserId == userId
              && c.LastScanOutcome == CompanyWatchCriterionScanOutcome.Scanned
              && h.CreatedAt > c.CriteriaChangedAt
              && (c.NewAdsSeenThrough == null || h.CreatedAt > c.NewAdsSeenThrough)
        join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
        where j.Status == JobAdStatus.Active
        select h;

    /// <summary>
    /// The count of <see cref="NewHits"/> per criterion, one grouped statement. A criterion with none is absent from
    /// the map; whether that absence is a zero or "not scanned" is the caller's to decide from the criterion's
    /// outcome. One (criterion, ad) is one row (the UNIQUE key), so the count is of distinct ads.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<CompanyWatchCriterionId, int>> CountPerCriterionAsync(
        IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var counts = await NewHits(db, userId)
            .GroupBy(h => h.CompanyWatchCriterionId)
            .Select(g => new { CriterionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(x => x.CriterionId, x => x.Count);
    }

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
