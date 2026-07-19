using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Queries.GetNewResultsCount;

/// <summary>
/// #312 (ADR 0115) — computes each notification-enabled saved search's "N nya träffar"-count LIVE
/// (no persisted hit rows): for each search, count ACTIVE ads matching its criteria with
/// <c>CreatedAt &gt; ResultsSeenAt</c>, via the shared <see cref="IJobAdSearchQuery"/> filter SPOT
/// (synonym-parity + the #864 <c>Status=Active</c> lifecycle guard inherited). Watermark-driven —
/// #293/#306's fixed "Ny"-window is retired. NO AI/LLM, no PII.
/// <para>
/// PERF (R1, TD-94) — this is a per-search fan-out (N × COUNT), the exact shape that forced
/// <c>ListRecentSearches</c>' live count OFF in production (Npgsql 57014, ADR 0045 budget break). It
/// is therefore (a) capped at <see cref="MaxSearchesScanned"/> most-recently-updated searches to
/// bound the fan-out (DoS defense — SavedSearch has no per-seeker create cap, unlike
/// <c>RecentJobSearch.MaxPerSeeker</c>), and (b) gated by a fitness function against ADR 0045
/// before FE go-live. If it fails the budget the FE evolves to lazy per-row fetch (ADR 0060's
/// useFacetCounts pattern), never a speculative materialized read-model.
/// </para>
/// </summary>
public sealed class GetNewSavedSearchResultsCountQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IJobAdSearchQuery search)
    : IQueryHandler<GetNewSavedSearchResultsCountQuery, IReadOnlyList<NewSavedSearchResultsCountDto>>
{
    // Bounds the per-search COUNT fan-out (R1/DoS): a user can create unbounded saved searches (no
    // per-seeker cap), so the count-all path scans at most this many notification-enabled searches,
    // most-recently-updated first. A user past this bound sees badges on their newest N (documented
    // degradation; the lazy-per-row FE evolution removes the batch entirely).
    private const int MaxSearchesScanned = 20;

    public async ValueTask<IReadOnlyList<NewSavedSearchResultsCountDto>> Handle(
        GetNewSavedSearchResultsCountQuery query, CancellationToken cancellationToken)
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

        // Notification-enabled searches (query filter auto-excludes soft-deleted), most-recently-
        // updated first, capped. Materialized BEFORE the fan-out loop — each CountNewSinceAsync opens
        // its own short transaction (bitmap-plan coax), so no open-reader conflict on this context.
        var searches = await db.SavedSearches
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId && s.NotificationEnabled)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(MaxSearchesScanned)
            // Project to just the fields the fan-out needs (not the whole aggregate) — §3.6/§5,
            // parity RunSavedSearchQueryHandler's `.Select(s => s.Criteria)`.
            .Select(s => new { s.Id, s.Name, s.Criteria, s.ResultsSeenAt, s.CreatedAt })
            .ToListAsync(cancellationToken);

        var results = new List<NewSavedSearchResultsCountDto>(searches.Count);
        foreach (var s in searches)
        {
            // The watermark coalesces defensively to the search's own creation (never epoch) if a
            // row predates the #312 backfill; in practice always set (ctor init + migration backfill).
            var since = s.ResultsSeenAt ?? s.CreatedAt;
            var count = await search.CountNewSinceAsync(
                SearchCriteriaMapping.ToFilterCriteria(s.Criteria), since, cancellationToken);
            results.Add(new NewSavedSearchResultsCountDto(s.Id.Value, s.Name, count));
        }

        return results;
    }
}
