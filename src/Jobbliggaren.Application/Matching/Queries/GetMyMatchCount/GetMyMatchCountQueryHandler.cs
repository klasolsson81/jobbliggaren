using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6 — bygger den autentiserade användarens DEK-fria profil (parity
/// list-sort-vägen, <c>BuildFullForSortAsync</c>) och räknar matchningarna i headline-grad-
/// setet via den delade per-användar-porten. SSYK-gate (parity GetJobAdMatchBatch /
/// ListJobAdsQueryHandler): inget angivet yrke → honest 0 (Översikts setup-nudge äger
/// "komplettera profil"-fallet; notisen visas då inte alls). NO AI/LLM; läser ingen CV/DEK/PII.
/// </summary>
public sealed class GetMyMatchCountQueryHandler(
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<GetMyMatchCountQuery, MyMatchCountDto>
{
    // Headline-grad-setet (Klas 2026-06-24): Bra + Stark (Good + Strong). MÅSTE förbli
    // koherent med FE-notisens länk (?matchGrades=Good&matchGrades=Strong) — counten ÄR den
    // länkade /jobb-sidans TotalCount per konstruktion (samma ApplyFilter-SPOT +
    // GradeRankExpression). Topp ingår aldrig (Fast-bandet, G3-OPT-A); rubriken är grad-neutral.
    private static readonly IReadOnlyList<MatchGrade> HeadlineGrades =
        [MatchGrade.Good, MatchGrade.Strong];

    // Notisen räknar över HELA den aktiva korpusen (ingen sök-/dimensions-filter) — bara
    // profil-graden gallrar. Tom filter-SPOT = alla Active annonser. Named arguments per
    // JobAdFilterCriteria:s konstruktions-kontrakt (sex listor i rad = tyst-fel-fälla).
    private static readonly JobAdFilterCriteria NoFilter =
        new(
            OccupationGroup: [],
            Municipality: [],
            Region: [],
            EmploymentType: [],
            WorktimeExtent: [],
            // #311 D6 — notisen filtrerar aldrig på arbetsgivare (grad-only).
            Employer: [],
            Q: null);

    public async ValueTask<MyMatchCountDto> Handle(
        GetMyMatchCountQuery query, CancellationToken cancellationToken)
    {
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);

        // Ingen användare / inget angivet yrke → tom SSYK → honest 0 (aldrig en fejkad
        // siffra; notisen visas inte, setup-nudgen äger slotten).
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return MyMatchCountDto.Zero;

        var count = await perUserSearch.CountPerUserAsync(
            NoFilter, profile, HeadlineGrades, cancellationToken);

        return new MyMatchCountDto(count);
    }
}
