using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetFacetCounts;

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
                // #311 D6 (ADR 0087): inget Employer-facett-stöd i PR-2. Employer är
                // INTE en FacetDimension (ADR 0087 D6 — får ej foldas in i
                // IJobAdSearchQuery; arbetsgivar-count görs av den separata
                // disambiguerings-projektionen, ej facett-machineriet). Det fanns
                // heller ingen Employer-filterkontext att respektera här i PR-2 (ingen
                // FE-konsument före #408) → []. Facett-interaktionen med ett aktivt
                // arbetsgivar-filter landar i PR-2b med disambiguerings-ytan.
                Employer: [],
                // #551 PR-B: remote-facett-kontexten sätts i Commit 2 (query.Remote).
                Remote: false,
                Q: parser.Parse(query.Q).ResidualQ),
            query.Dimension,
            cancellationToken);
}
