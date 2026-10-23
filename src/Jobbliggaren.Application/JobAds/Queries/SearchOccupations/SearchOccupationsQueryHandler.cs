using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.SearchOccupations;

/// <summary>A thin call to the searcher port; the searcher owns the rule, the order and the cap.</summary>
public sealed class SearchOccupationsQueryHandler(IOccupationSearcher searcher)
    : IQueryHandler<SearchOccupationsQuery, OccupationSearchResult>
{
    public ValueTask<OccupationSearchResult> Handle(
        SearchOccupationsQuery query, CancellationToken cancellationToken) =>
        searcher.SearchAsync(query.Query, cancellationToken);
}
