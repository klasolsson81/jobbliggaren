using JobbPilot.Application.Common;
using JobbPilot.Application.JobAds.Abstractions;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

/// <summary>
/// Tunn adapter (ADR 0062): mappar <see cref="ListJobAdsQuery"/> till
/// <see cref="JobAdSearchCriteria"/> och delegerar till <see cref="IJobAdSearchQuery"/>.
/// Hela sök-kompositionen (filter, FTS, sort, paginering) bor i Infrastructure-
/// impl:en bakom porten — ADR 0039 Beslut 1 SPOT delas med
/// <c>RunSavedSearchQueryHandler</c>.
/// </summary>
public sealed class ListJobAdsQueryHandler(IJobAdSearchQuery search)
    : IQueryHandler<ListJobAdsQuery, PagedResult<JobAdDto>>
{
    public ValueTask<PagedResult<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
        => search.SearchAsync(
            new JobAdSearchCriteria(
                // null → tom lista: "inget filter" (ADR 0042 Beslut B).
                // ADR 0067 Beslut 1 (Variant C): pure-adapter passthrough av alla
                // dimensioner inkl. Ssyk (occupation-name). No-op-disciplinen för
                // Ssyk enforce:as på ETT ställe — ApplyCriteria har ingen Ssyk-
                // equality-gren längre. Alla tre konsumenter (ListJobAds/
                // RunSavedSearch/ListRecentSearches) matar sin Ssyk-källa hit
                // enhetligt; q-vägens synonym-expansion mot SsykConceptId drivs
                // separat av Q.
                new JobAdFilterCriteria(
                    OccupationGroup: query.OccupationGroup ?? [],
                    Municipality: query.Municipality ?? [],
                    Region: query.Region ?? [],
                    Ssyk: query.Ssyk ?? [],
                    Q: query.Q),
                query.SortBy,
                query.Page,
                query.PageSize,
                query.Since),
            cancellationToken);
}
