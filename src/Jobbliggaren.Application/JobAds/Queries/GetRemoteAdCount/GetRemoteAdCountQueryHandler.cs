using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — thin adapter (parity <c>GetFacetCountsQueryHandler</c>): maps the
/// non-location filter context to a <see cref="JobAdFilterCriteria"/> with
/// <c>Remote: true</c> and the LOCATION dimension emptied, then delegates to the shared
/// filter-SPOT <see cref="IJobAdSearchQuery.CountAsync"/>. No new port method — the
/// facet-excluded remote count IS a total count over a remote-forced criteria.
/// <para>
/// <c>Municipality</c>/<c>Region</c> are <c>[]</c> unconditionally (the query does not even
/// carry them — location is the excluded facet, D7). <c>Employer: []</c> parity
/// <c>GetFacetCounts</c> (no employer facet). <c>Q</c> runs the SAME residual normalization
/// as the list/facet paths (<see cref="ISearchQueryParser"/>) so the hint counts against the
/// same WHERE the list would (residual consistency, ADR 0067 Fas D2).
/// </para>
/// </summary>
public sealed class GetRemoteAdCountQueryHandler(
    IJobAdSearchQuery search, ISearchQueryParser parser)
    : IQueryHandler<GetRemoteAdCountQuery, RemoteAdCountDto>
{
    public async ValueTask<RemoteAdCountDto> Handle(
        GetRemoteAdCountQuery query, CancellationToken cancellationToken)
    {
        var filter = new JobAdFilterCriteria(
            OccupationGroup: query.OccupationGroup ?? [],
            // Location dimension EXCLUDED (D7 — remote ads are location-less; counting
            // remote ∧ muni would return ≈0 and lie about how many distansjobb exist).
            Municipality: [],
            Region: [],
            EmploymentType: query.EmploymentType ?? [],
            WorktimeExtent: query.WorktimeExtent ?? [],
            Employer: [],
            // The count predicate: "how many REMOTE ads match the rest".
            Remote: true,
            Q: parser.Parse(query.Q).ResidualQ);

        var count = await search.CountAsync(filter, cancellationToken);
        return new RemoteAdCountDto(count);
    }
}
