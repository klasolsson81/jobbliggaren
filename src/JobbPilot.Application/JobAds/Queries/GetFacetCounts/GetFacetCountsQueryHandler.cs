using JobbPilot.Application.JobAds.Abstractions;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.GetFacetCounts;

/// <summary>
/// Tunn adapter (ADR 0062-mönstret, spegelbild av <c>ListJobAdsQueryHandler</c>):
/// null → tom lista, residual-normalisering av Q, delegera till porten.
/// <para>
/// <b>Residual-konsistens (E2c-architect §2 — korrekthetskrav, inte stilval):</b>
/// facett-counts svarar på "vad får jag om jag väljer X givet mitt nuvarande
/// sök". Samma live-input ⇒ samma normalisering som list-vägen
/// (<see cref="ISearchQueryParser"/>, ADR 0067 Fas D2) — annars räknar
/// facetten mot en annan WHERE än listan för samma användar-input
/// ("Solna (12)" men listan visar 14). SPOT (Hunt/Thomas).
/// </para>
/// </summary>
public sealed class GetFacetCountsQueryHandler(
    IJobAdSearchQuery search, ISearchQueryParser parser)
    : IQueryHandler<GetFacetCountsQuery, IReadOnlyDictionary<string, int>>
{
    public ValueTask<IReadOnlyDictionary<string, int>> Handle(
        GetFacetCountsQuery query, CancellationToken cancellationToken)
        => search.FacetCountsAsync(
            new JobAdFilterCriteria(
                OccupationGroup: query.OccupationGroup ?? [],
                Municipality: query.Municipality ?? [],
                Region: query.Region ?? [],
                EmploymentType: query.EmploymentType ?? [],
                WorktimeExtent: query.WorktimeExtent ?? [],
                Q: parser.Parse(query.Q).ResidualQ),
            query.Dimension,
            cancellationToken);
}
