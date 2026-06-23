using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5) — <see cref="IMatchSortedJobAdSearchQuery"/>:
/// global "Sortera efter matchning". SEPARAT från <see cref="JobAdSearchQuery"/>
/// (Decision 5 — den delade <see cref="IJobAdSearchQuery"/> förblir match-ren);
/// återanvänder dock EXAKT samma filter-SPOT (<see cref="JobAdSearchComposition.ApplyFilter"/>)
/// + items-projektion (<see cref="JobAdSearchComposition.ToDto"/>) så match-sorten
/// aldrig träffar en annan annons-mängd än default-sorten.
/// <para>
/// <b>Sort-nyckeln (grad-ranken) lever ENBART i <c>ORDER BY</c></b> (Goodhart,
/// Decision 4): den projiceras aldrig in i <see cref="JobAdDto"/>, persisteras
/// aldrig. Ranken är en kompilerad spegel av den <b>Fast</b>
/// <c>MatchGradeCalculator.Grade(MatchScore)</c>-stegen + <c>MatchScorer.ScoreMembership</c>
/// (yrke/anställning) + <c>MatchScorer.ScoreOrtUnion</c> (ort = region ∪ kommun, Spår 3):
/// <para>
/// <b>G3-OPT-A — sorten ser INTE must-have (medveten, bunden divergens, ADR 0076
/// amendment 2026-06-20):</b> sedan graden blev requirement-aware
/// (<c>Grade(FullMatchScore)</c> gatar Strong/Top på must-have-täckning) är denna sort en
/// snabb, grov Fast-band-coarsening som ärligt SKILJER SIG från den synliga graden i
/// must-have-bandet — den heta vägen läser den bekräftade skill-mängden (plaintext, ingen
/// DEK, binär <c>?|</c>; ADR 0079 STEG 3 PR-D), kan inte beräkna must-have-täckning, och ska
/// därför inte påstå sig spegla den synliga graden. UI-copy säger "Sortera efter matchning"
/// (preferens+skill-relevans),
/// aldrig "sortera efter grad exakt". Ett orakel pinnar Fast-spegeln; ett separat test
/// pinnar divergensen (MatchSortOracleTests).</para>
/// Fast-rank-spegeln:
/// <list type="bullet">
/// <item>0 = otaggad (SSYK ej Match) → sorteras sist;</item>
/// <item>1 = Basic (SSYK Match, men en angiven ort (region/kommun) eller
/// anställningsform motsäger — <c>NoMatch</c>, golvar);</item>
/// <item>1 + antal bekräftade sekundärer (ort (region∪kommun) / anställningsform
/// <c>Match</c>) = 1/2/3 (Basic/Good/Strong); ort räknas som ETT sekundär (region- ELLER
/// kommun-träff). En <c>NotAssessed</c>-dimension (tom preferens ELLER ad utan ort-värde)
/// varken bekräftar eller golvar.</item>
/// </list>
/// Tie-break: <c>publishedAt</c> fallande, sedan <c>Id</c> (determinism).
/// Ett Testcontainers-orakel pinnar SQL-rank ≡ <c>MatchGradeCalculator</c> över
/// hela verdict-tuple-rymden (InMemory döljer translationen — samma
/// <c>ef_strongly_typed_vo_contains</c>-lärdom; <c>= ANY</c>-translationen av
/// <c>list.Contains(EF.Property)</c> är samma som körs i ApplyFilter i prod).
/// </para>
/// </summary>
internal sealed class MatchSortedJobAdSearchQuery(
    AppDbContext db,
    IOccupationSynonymExpander synonymExpander,
    IJobAdSearchQuery searchQuery) : IMatchSortedJobAdSearchQuery
{
    // STORED shadow-kolumner (EF.Property-nycklar) — parity MatchScorer; kolumn-
    // namnen är en Infrastructure-hemlighet som aldrig läcker till Application.
    private const string OccupationGroupColumn = "OccupationGroupConceptId";
    private const string RegionColumn = "RegionConceptId";
    private const string EmploymentTypeColumn = "EmploymentTypeConceptId";

    // Spår 3 (ADR 0076-amendment 2026-06-21) — kommun-granulariteten i ort-dimensionen.
    // Sort-ranken speglar nu MatchScorer.ScoreOrtUnion (region ∪ municipality) i stället
    // för region-only ScoreMembership. STORED generated från
    // raw_payload->'workplace_address'->>'municipality_concept_id' (parity scorern).
    private const string MunicipalityColumn = "MunicipalityConceptId";

    // STORED generated jsonb companion (extracted_lexemes = Lexeme-array, GIN-indexerad)
    // — bär skill-concept-ids (Lexeme == ConceptId för Skill/Requirement-termer). F4-15:s
    // gyllene rung testar `extracted_lexemes ?| @cvSkillIds` (EF.Functions.JsonExistAny).
    private const string ExtractedLexemesColumn = "ExtractedLexemes";

    public async ValueTask<PagedResult<JobAdDto>> SearchByMatchAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        int page,
        int pageSize,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(profile);

        // Count är sort-oberoende → återanvänd den rena port-counten (TD-94
        // bitmap-plan ingår). Ingen duplicerad count-väg (DRY, ADR 0039 Beslut 1).
        var totalCount = await searchQuery.CountAsync(filter, cancellationToken);

        // Profil-listorna fångas lokalt → EF binder dem som parametrar (= ANY).
        // SSYK är icke-tom (handlerns gate). regions/employment kan vara tomma
        // (NotAssessed — varken bekräftar eller golvar, MatchScorer.ScoreMembership).
        var fast = profile.Fast;
        var ssyk = fast.SsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        var employment = fast.PreferredEmploymentTypeConceptIds;
        // The ort dimension folds region ∪ municipality into ONE secondary (parity
        // RegionFit / ScoreOrtUnion): "stated" iff EITHER preference list is non-empty.
        var ortStated = regions.Count > 0 || municipalities.Count > 0;
        var employmentStated = employment.Count > 0;

        // F4-15 (ADR 0076 Decision 6) → ADR 0079 STEG 3 PR-D: den gyllene topp-rungen.
        // CvSkillConceptIds är nu den BEKRÄFTADE skill-mängden (plaintext PreferredSkills,
        // ingen DEK) — SAMMA källa som verdikt-scorern, så sort och grad kan aldrig divergera
        // på en borttagen skill (ingen fel-lyft). Tom mängd → skillStated == false → den
        // gyllene termen blir konstant 0 (EF prunes) → ordning ≡ F4-14.
        var cvSkillIds = profile.CvSkillConceptIds.ToArray();
        var skillStated = cvSkillIds.Length > 0;

        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        var items = await baseQuery
            // Gyllene topp-rung (F4-15): en Stark match (yrke+ort+anställning ALLA
            // bekräftade — samma villkor som grad-rank == 3) som OCKSÅ delar minst en
            // bekräftad skill (`extracted_lexemes ?| @cvSkillIds`) sorteras ÖVER en ren Stark.
            // Ort-bekräftelsen är union (region ELLER kommun träffar, parity ScoreOrtUnion).
            // Den bekräftade mängden är komplett per definition (ADR 0079 PR-D) → ingen
            // top-5-under-recall kvar. NULL extracted_lexemes → ?| NULL → ELSE 0.
            .OrderByDescending(j =>
                skillStated
                && ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                && (regions.Contains(EF.Property<string?>(j, RegionColumn))
                    || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn)))
                && employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))
                && EF.Functions.JsonExistAny(EF.Property<string>(j, ExtractedLexemesColumn), cvSkillIds)
                    ? 1
                    : 0)
            // Grad-rank fallande (3=Strong … 0=otaggad sist). NotAssessed≠NoMatch:
            // ort "motsäger" kräver en ANGIVEN ort-preferens (ortStated) OCH att annonsen
            // HAR minst ett ort-värde (region ELLER kommun icke-NULL) OCH ingen union-träff.
            // IMPL-TRAP (CTO C): det är det KOMBINERADE predikatet — ALDRIG ett naket
            // !municipalities.Contains(col), som skulle läsa en NULL kommun-shadow som
            // "inte i listan" (den klassiska !list.Contains(col)-buggen). Ett tomt
            // preferens-set ger Contains == false (= NotAssessed → bidrar 0, golvar ej).
            // Ort-bekräftelse = region-träff ELLER kommun-träff = ETT sekundär (parity
            // RegionFit). Speglar MatchScorer.ScoreOrtUnion + MatchGradeCalculator exakt
            // (oracle-pinnad).
            .ThenByDescending(j =>
                !ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                    ? 0
                    : ((ortStated
                            && (EF.Property<string?>(j, RegionColumn) != null
                                || EF.Property<string?>(j, MunicipalityColumn) != null)
                            && !(regions.Contains(EF.Property<string?>(j, RegionColumn))
                                || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))))
                        || (employmentStated
                            && EF.Property<string?>(j, EmploymentTypeColumn) != null
                            && !employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))))
                        ? 1
                        : 1
                            + ((regions.Contains(EF.Property<string?>(j, RegionColumn))
                                || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))) ? 1 : 0)
                            + (employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn)) ? 1 : 0))
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(JobAdSearchComposition.ToDto(since))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, page, pageSize);
    }
}
