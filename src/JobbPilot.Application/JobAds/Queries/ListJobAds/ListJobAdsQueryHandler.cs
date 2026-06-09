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
                // ADR 0067 Fas C2 (CTO-dom (e)): Ssyk-dimensionen borttagen ur
                // SPOT:en — q-vägens synonym-expansion mot SsykConceptId drivs
                // separat av Q (recall-substratet orört).
                new JobAdFilterCriteria(
                    OccupationGroup: query.OccupationGroup ?? [],
                    Municipality: query.Municipality ?? [],
                    Region: query.Region ?? [],
                    Q: query.Q),
                query.SortBy,
                query.Page,
                query.PageSize,
                query.Since),
            cancellationToken);
}
