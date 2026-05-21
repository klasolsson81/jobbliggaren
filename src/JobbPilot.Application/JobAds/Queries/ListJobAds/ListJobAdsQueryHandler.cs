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
                new JobAdFilterCriteria(query.Ssyk ?? [], query.Region ?? [], query.Q),
                query.SortBy,
                query.Page,
                query.PageSize,
                query.Since),
            cancellationToken);
}
