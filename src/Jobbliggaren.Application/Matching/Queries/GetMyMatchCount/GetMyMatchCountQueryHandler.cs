using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6, HARMONISERAD 2026-07-03 (Klas-ruling "samma siffra, inget brus";
/// senior-cto-advisor bind H2) — notis-counten är en REN SÖK-FACETT-COUNT över den
/// sparade matchningens val (yrke ∧ ort ∧ anställningsform som HÅRDA filter), via
/// exakt samma port + SPOT som setup-räknaren (<see cref="IJobAdSearchQuery.CountAsync"/>
/// → <c>JobAdSearchComposition.ApplyFilter</c>, parity <c>GetMatchCountPreviewQueryHandler</c>).
/// Grad-bandet (Bra/Stark) är BORTTAGET ur notis-vägen: med orter/former som hårda krav
/// var bandet redundant för hel-angivna profiler och kollapsade yrke-bara-profiler till 0.
/// Graden lever vidare oförändrad som badges, match-sort och bakgrundsmatchning på /jobb.
/// <para>
/// Talet är därmed per konstruktion lika med BÅDE setup-modalens live-räknare och den
/// länkade /jobb-sidans <c>TotalCount</c> för samma facetter (länken bär facetterna, inga
/// <c>matchGrades</c>). SSYK-gate kvar (parity förr): inget angivet yrke → honest 0
/// (Översikts setup-nudge äger "komplettera profil"-fallet; notisen visas då inte alls).
/// NO AI/LLM; läser ingen CV/DEK/PII.
/// </para>
/// </summary>
public sealed class GetMyMatchCountQueryHandler(
    IMatchProfileBuilder profileBuilder,
    IJobAdSearchQuery search)
    : IQueryHandler<GetMyMatchCountQuery, MyMatchCountDto>
{
    public async ValueTask<MyMatchCountDto> Handle(
        GetMyMatchCountQuery query, CancellationToken cancellationToken)
    {
        // DEK-fria SORT-profilen bär de sparade facetterna (Fast-listorna är
        // MatchPreferences.Preferred* fält-för-fält, includeRelated=false →
        // ingen related-expansion; samma set som FE-länken bär).
        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);

        // Ingen användare / inget angivet yrke → tom SSYK → honest 0 (aldrig en
        // fejkad siffra; notisen visas inte, setup-nudgen äger slotten).
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return MyMatchCountDto.Zero;

        // #551 PR-B F3 — den sparade remote/distans-preferensen matar count:ens location-axel
        // (mechanism B). Läses via den dedikerade builder-metoden EFTER SSYK-grinden (bara för
        // hel-angivna profiler) — ALDRIG via profile.Fast (F1: scorern får aldrig se den;
        // ADR 0079 never-grade-coupled, arch-pinnad av MatchProfileRemoteIndependenceTests).
        var preferredRemote =
            await profileBuilder.GetPreferredRemoteForNotificationCountAsync(cancellationToken);

        // Sparade val som HÅRDA filter (H2). Named arguments per
        // JobAdFilterCriteria:s konstruktions-kontrakt (sex listor i rad =
        // tyst-fel-fälla). WorktimeExtent/Employer/Q är tomma/null —
        // matchningen exponerar inte de dimensionerna.
        var filter = new JobAdFilterCriteria(
            OccupationGroup: profile.Fast.SsykGroupConceptIds,
            Municipality: profile.Fast.PreferredMunicipalityConceptIds,
            Region: profile.Fast.PreferredRegionConceptIds,
            EmploymentType: profile.Fast.PreferredEmploymentTypeConceptIds,
            WorktimeExtent: [],
            Employer: [],
            // #551 PR-B F3 — den sparade remote-preferensen som location-axel (unionas med
            // ort i ApplyFilter). Ur den dedikerade builder-metoden, ALDRIG ur profile.Fast.
            Remote: preferredRemote,
            Q: null);

        var count = await search.CountAsync(filter, cancellationToken);
        return new MyMatchCountDto(count);
    }
}
