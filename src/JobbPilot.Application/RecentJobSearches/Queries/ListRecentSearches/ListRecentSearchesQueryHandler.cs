using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.RecentJobSearches.Queries.ListRecentSearches;

/// <summary>
/// ADR 0060 — list-projektion för auto-fångade RecentJobSearches per JobSeeker.
/// Avsiktlig N+1 i CurrentCount-loopen (CTO 2026-05-20 Variant A): cap=20
/// (<c>RecentJobSearch.MaxPerSeeker</c>) håller fan-out hanterbart; varje
/// träffräkning går via <see cref="IJobAdSearchQuery.CountAsync"/> (ADR 0062 —
/// samma filter-SPOT som ListJobAds, q-FTS-accelererad). Fitness function
/// (ADR 0045) övervakar p95 och triggar Hangfire-cache-evolution om budget bryts.
///
/// <para>Label server-härleds (Q || ssykLabels.First || regionLabels.First ||
/// fallback) så FE inte behöver konstruera presentation. Defensive fallback
/// "Alla annonser" är dead code så länge SearchCriteria.Empty-invarianten
/// håller, men behålls för robusthet.</para>
/// </summary>
public sealed class ListRecentSearchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IJobAdSearchQuery search)
    : IQueryHandler<ListRecentSearchesQuery, IReadOnlyList<RecentJobSearchDto>>
{
    public async ValueTask<IReadOnlyList<RecentJobSearchDto>> Handle(
        ListRecentSearchesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        var items = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId)
            .OrderByDescending(r => r.LastViewedAt)
            .ToListAsync(cancellationToken);

        var dtos = new List<RecentJobSearchDto>(items.Count);
        foreach (var r in items)
        {
            var ssykLabels = await taxonomy.ResolveLabelsAsync(r.Ssyk, cancellationToken);
            var regionLabels = await taxonomy.ResolveLabelsAsync(r.Region, cancellationToken);

            // Live-count: avsiktlig N+1 capped vid MaxPerSeeker=20 (CTO Variant A
            // Q3-villkor). Via IJobAdSearchQuery.CountAsync (ADR 0062 — delad
            // filter-SPOT med ListJobAds, q-FTS-accelererad). ADR 0045 fitness
            // function observerar p95.
            var currentCount = await search.CountAsync(
                new JobAdFilterCriteria(r.Ssyk, r.Region, r.Q), cancellationToken);

            var newCount = Math.Max(0, currentCount - r.LastSeenCount);
            var label = DeriveLabel(r.Q, ssykLabels, regionLabels);

            dtos.Add(new RecentJobSearchDto(
                r.Id.Value,
                r.Q,
                r.Ssyk,
                r.Region,
                ssykLabels,
                regionLabels,
                r.SortBy,
                label,
                currentCount,
                newCount,
                r.LastViewedAt));
        }

        return dtos;
    }

    private static string DeriveLabel(
        string? q,
        IReadOnlyList<TaxonomyLabelDto> ssykLabels,
        IReadOnlyList<TaxonomyLabelDto> regionLabels)
    {
        if (!string.IsNullOrWhiteSpace(q))
            return q;
        if (ssykLabels.Count > 0)
            return ssykLabels[0].Label;
        if (regionLabels.Count > 0)
            return regionLabels[0].Label;
        return "Alla annonser";
    }
}
