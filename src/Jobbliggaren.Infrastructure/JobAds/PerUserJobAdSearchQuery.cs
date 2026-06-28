using System.Linq.Expressions;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// ADR 0076 Decision 4/5 + ADR 0079 STEG 5 — <see cref="IPerUserJobAdSearchQuery"/>:
/// den per-användar-sökvägen för <c>/jobb</c>. SEPARAT från
/// <see cref="JobAdSearchQuery"/> (Decision 5 — den delade
/// <see cref="IJobAdSearchQuery"/> förblir match-ren och anonymt cachebar);
/// återanvänder dock EXAKT samma filter-SPOT (<see cref="JobAdSearchComposition.ApplyFilter"/>),
/// rena sort (<see cref="JobAdSearchComposition.ApplySort"/>) + items-projektion
/// (<see cref="JobAdSearchComposition.ToDto"/>) så grad-filtret/match-sorten aldrig
/// träffar en annan annons-mängd än default-sorten.
/// <para>
/// <b>Grad-filter + sort frikopplade (ADR 0079 STEG 5 re-bind 2026-06-23):</b> ett
/// grad-WHERE (Fast-bandet) gallrar mängden; ordningen är ANTINGEN match-rank
/// (<c>orderByMatchRank</c> — grad fallande + gyllene skill-rung) ELLER en ren axel
/// (senast inlagda / kortast ansökningstid / relevans) via den delade ApplySort. EN
/// delad <see cref="GradeRankExpression"/> driver grad-WHERE, count OCH
/// match-rank-ORDER BY (DRY, Hunt/Thomas 1999) — filtret och sorten kan aldrig divergera.
/// </para>
/// <para>
/// <b>Sort-nyckeln/grad-ranken lever ENBART i <c>WHERE</c>/<c>ORDER BY</c></b> (Goodhart,
/// Decision 4): aldrig projicerad in i <see cref="JobAdDto"/>, aldrig persisterad. Ranken
/// speglar den <b>Fast</b> <c>MatchGradeCalculator.Grade(MatchScore)</c>-stegen +
/// <c>MatchScorer.ScoreMembership</c> (yrke/anställning) + <c>MatchScorer.ScoreOrtUnion</c>
/// (ort = region ∪ kommun, Spår 3):
/// <list type="bullet">
/// <item>0 = otaggad (SSYK ∉ exact ∪ related) → exkluderas av grad-filtret (positiv-only),
/// sorteras sist i match-rank;</item>
/// <item>2 = Related (#300 PR-4, ADR 0084 §F2 — SSYK i den RELATERADE/substituerbara mängden
/// men EJ i den angivna exakta; platt cap MELLAN Basic och Good, oberoende av sekundärer);</item>
/// <item>1 = Basic (exakt SSYK Match, men en angiven ort/anställningsform motsäger — golvar);</item>
/// <item>exakt SSYK Match utan motsägelse: 1/3/4 (Basic/Good/Strong) efter antal bekräftade
/// sekundärer (ort (region∪kommun) / anställningsform) — 0/1/2 st. En <c>NotAssessed</c>-
/// dimension varken bekräftar eller golvar.</item>
/// </list>
/// <b>G3-OPT-A (medveten, bunden divergens):</b> Fast-bandet toppar på Stark — Topp kan
/// inte beräknas i SQL (ingen kind-separerad must-have-lexem-kolumn) och avvisas wire-side
/// av validatorn. Ett Testcontainers-orakel pinnar SQL-rank ≡ <c>MatchGradeCalculator</c>
/// över hela verdict-tuple-rymden (InMemory döljer <c>= ANY</c>-translationen —
/// <c>ef_strongly_typed_vo_contains</c>-lärdomen).
/// </para>
/// </summary>
internal sealed class PerUserJobAdSearchQuery(
    AppDbContext db,
    IOccupationSynonymExpander synonymExpander,
    IJobAdSearchQuery searchQuery) : IPerUserJobAdSearchQuery
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

    public async ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        JobAdSortBy sort,
        bool orderByMatchRank,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grades);

        // Profil-listorna fångas lokalt → EF binder dem som parametrar (= ANY).
        // SSYK är icke-tom (handlerns gate). regions/employment kan vara tomma
        // (NotAssessed — varken bekräftar eller golvar, MatchScorer.ScoreMembership).
        var fast = profile.Fast;
        var ssyk = fast.SsykGroupConceptIds;
        // #300 PR-4 (ADR 0084 §F4): the RELATED ssyk-4 set (substitutable occupations). Empty
        // until the PR-5 toggle populates it, so the Related branch in GradeRankExpression is
        // inert in v1 (every ad either gates out or scores via the exact set, byte-for-byte today).
        var relatedSsyk = fast.RelatedSsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        var employment = fast.PreferredEmploymentTypeConceptIds;
        // The ort dimension folds region ∪ municipality into ONE secondary (parity
        // RegionFit / ScoreOrtUnion): "stated" iff EITHER preference list is non-empty.
        var ortStated = regions.Count > 0 || municipalities.Count > 0;
        var employmentStated = employment.Count > 0;

        // F4-15 (ADR 0076 Decision 6) → ADR 0079 STEG 3 PR-D: den gyllene topp-rungen.
        // CvSkillConceptIds är den BEKRÄFTADE skill-mängden (plaintext PreferredSkills,
        // ingen DEK) — SAMMA källa som verdikt-scorern, så sort och grad aldrig divergerar
        // på en borttagen skill. Tom mängd → skillStated == false → gyllene termen blir
        // konstant 0 (EF prunes). OBS (#300 PR-4): den gyllene termen läser den EXAKTA ssyk-
        // mängden — en related-only-annons (Related-rank) får ALDRIG den gyllene lyften.
        var cvSkillIds = profile.CvSkillConceptIds.ToArray();
        var skillStated = cvSkillIds.Length > 0;

        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        // EN delad rank-Expression (DRY) — speglar MatchGradeCalculator.Grade(MatchScore).
        // Konsumeras av grad-WHERE, count OCH match-rank-ORDER BY så filter och sort aldrig
        // divergerar (CTO-re-bind 2026-06-23). Oracle-pinnad.
        var rankExpr = GradeRankExpression(
            ssyk, relatedSsyk, regions, municipalities, employment, ortStated, employmentStated);

        // ADR 0079 STEG 5 — grad-WHERE: den PER-ANVÄNDAR-predikaten. Lever ENBART här,
        // aldrig i den delade anonymt cachebara ApplyFilter (anon-cache + SavedSearch +
        // recent-search-isolering, ADR 0039 Beslut 1 / 0062). Positiv-only: rank 0
        // (otaggad) är aldrig valbar → exkluderas så snart en grad valts.
        var graded = baseQuery;
        if (grades.Count > 0)
        {
            var selectedRanks = grades.Select(GradeToRank).Distinct().ToArray();
            graded = graded.Where(RankInSet(rankExpr, selectedRanks));
        }

        // Count-korrekthet (CTO-re-bind rad-86-fix): när grad-WHERE är aktiv MÅSTE
        // total-antalet räknas om över den grad-filtrerade mängden — annars överräknar
        // den delade port-counten → spök-paginering. Separat count-query (CLAUDE §3.6).
        // Tom grad-mängd ⇒ port-counten är exakt rätt (och bär TD-94-bitmap-plan-hygienen).
        var totalCount = grades.Count > 0
            ? await graded.CountAsync(cancellationToken)
            : await searchQuery.CountAsync(filter, cancellationToken);

        // Ordning: match-rank (gyllene rung + grad-rank) när användaren valt "bästa
        // matchning"; annars den rena delade sorten (senast inlagda / kortast
        // ansökningstid / relevans) över den grad-filtrerade mängden.
        var ordered = orderByMatchRank
            ? graded
                // Gyllene topp-rung (F4-15): en Stark match (yrke+ort+anställning ALLA
                // bekräftade) som OCKSÅ delar minst en bekräftad skill sorteras ÖVER en ren
                // Stark. NULL extracted_lexemes → ?| NULL → ELSE 0.
                // OBS (#268 audit, C3): extracted_lexemes är jsonb_path_query_array över
                // `$[*].Lexeme` — den skördar ALLA termers Lexeme oavsett Kind (Skill ∪
                // must_have ∪ nice_to_have ∪ keyword; för Skill/Requirement är Lexeme ==
                // ConceptId). Den här sort-lyften är därför ett STRIKT SUPERSET av badgens
                // skill-signal (MatchGradeCalculator.HasSkillOrNiceSignal, som bara räknar
                // Kind==Skill / Source==NiceToHave). En CV-skill som matchar en annons
                // must_have-only-concept-id (ej ekad i fritext) lyfter alltså sorten men
                // badgar "Stark match", aldrig "Toppmatch". Medvetet en-riktat: sorten är
                // aldrig sämre informerad än badgen (en must_have-träff ÄR kravevidens).
                .OrderByDescending(j =>
                    skillStated
                    && ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                    && (regions.Contains(EF.Property<string?>(j, RegionColumn))
                        || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn)))
                    && employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))
                    && EF.Functions.JsonExistAny(EF.Property<string>(j, ExtractedLexemesColumn), cvSkillIds)
                        ? 1
                        : 0)
                // Grad-rank fallande (4=Strong, 3=Good, 2=Related, 1=Basic, 0=otaggad sist —
                // #300 PR-4-omnumrering) — samma delade Expression som grad-WHERE/count
                // (DRY, oracle-pinnad).
                .ThenByDescending(rankExpr)
                .ThenByDescending(j => j.PublishedAt)
                .ThenBy(j => j.Id)
            : JobAdSearchComposition.ApplySort(graded, sort, filter.Q);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(JobAdSearchComposition.ToDto())
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, page, pageSize);
    }

    public async ValueTask<int> CountPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grades);

        var fast = profile.Fast;
        var ssyk = fast.SsykGroupConceptIds;
        // #300 PR-4 (ADR 0084 §F4): MÅSTE passera samma relatedSsyk som SearchPerUserAsync —
        // annars divergerar count från list-vägens TotalCount (spök-paginering). Tom i v1.
        var relatedSsyk = fast.RelatedSsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        var employment = fast.PreferredEmploymentTypeConceptIds;
        var ortStated = regions.Count > 0 || municipalities.Count > 0;
        var employmentStated = employment.Count > 0;

        // SAMMA filter-SPOT + SAMMA delade GradeRankExpression som SearchPerUserAsync:s
        // grad-WHERE → counten är per konstruktion lika med list-vägens TotalCount för
        // samma profil + grad-set (siffra↔landning-koherens, oracle-pinnad). Separat
        // count-query (CLAUDE §3.6): ingen sort, paginering eller projektion.
        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        if (grades.Count == 0)
            // Tom-grades-grenen utelämnar MEDVETET TD-94:s bitmap-plan-hygien (SET LOCAL
            // enable_seqscan=off) som list-vägens delade port-count bär: den nås bara med
            // rena equality-filter (ingen q-FTS — STEG 6-notisen skickar alltid icke-tom
            // HeadlineGrades + NoFilter med Q==null), så TOAST-detoast-seqscan över
            // search_vector biter inte här. Cardinaliteten är ändå identisk (samma
            // ApplyFilter-SPOT).
            return await baseQuery.CountAsync(cancellationToken);

        var rankExpr = GradeRankExpression(
            ssyk, relatedSsyk, regions, municipalities, employment, ortStated, employmentStated);
        var selectedRanks = grades.Select(GradeToRank).Distinct().ToArray();
        return await baseQuery
            .Where(RankInSet(rankExpr, selectedRanks))
            .CountAsync(cancellationToken);
    }

    // Den delade grad-rank-Expression:en (SSOT) — en kompilerad spegel av den Fast
    // MatchGradeCalculator.Grade(MatchScore, isRelated)-stegen över shadow-kolumnerna.
    // #300 PR-4 (ADR 0084 §F2/§F4): Related infogad MELLAN Basic och Good → rank-omnumrering.
    // Returnerar 0 (otaggad/SSYK ∉ exact ∪ related), 1 (Basic — golv vid motsägande ort/
    // anställning ELLER inga bekräftade sekundärer), 2 (Related — yrket är en SUBSTITUERBAR
    // granne, ej i den angivna exakta mängden; platt cap), 3 (Good — en bekräftad sekundär),
    // 4 (Strong — båda). Gren-ordningen speglar calculatorns cap-ordning EXAKT: gate →
    // Related-cap (FÖRE RB1) → RB1-golv → sekundärer. En related-only-annons i fel stad läser
    // därför Related (2), ALDRIG Basic (1). IMPL-TRAP (CTO C): motsägelse-golvet är ett
    // KOMBINERAT predikat (angiven preferens OCH annonsen har ett ort-/anställnings-värde OCH
    // ingen union-träff) — aldrig ett naket !list.Contains(col), som skulle läsa en NULL-shadow
    // som "inte i listan". relatedSsyk är tom i v1 (PR-5-toggeln fyller den) → Related-grenen
    // inert, exact-grenarna byte-for-byte som förr (bara rank-heltalen omnumrerade; GradeToRank
    // omnumreras likadant så filter/sort/count förblir koherenta — oracle-pinnat).
    private static Expression<Func<JobAd, int>> GradeRankExpression(
        IReadOnlyList<string> ssyk,
        IReadOnlyList<string> relatedSsyk,
        IReadOnlyList<string> regions,
        IReadOnlyList<string> municipalities,
        IReadOnlyList<string> employment,
        bool ortStated,
        bool employmentStated) =>
        j =>
            // Gate: yrket måste vara i exact ∪ related, annars otaggad (0). Gaten vinner över
            // Related-cap (parity calculatorns gate-före-cap).
            !(ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                || relatedSsyk.Contains(EF.Property<string?>(j, OccupationGroupColumn)))
                ? 0
                // Related-cap (platt, FÖRST efter gaten — FÖRE RB1-golvet): i unionen men EJ i
                // den exakta mängden → related-only → Related (2).
                : !ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                    ? 2
                    // Exakt träff. RB1-motsägelse-golv → Basic (1).
                    : ((ortStated
                            && (EF.Property<string?>(j, RegionColumn) != null
                                || EF.Property<string?>(j, MunicipalityColumn) != null)
                            && !(regions.Contains(EF.Property<string?>(j, RegionColumn))
                                || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))))
                        || (employmentStated
                            && EF.Property<string?>(j, EmploymentTypeColumn) != null
                            && !employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))))
                        ? 1
                        // Exakt träff, ingen motsägelse: båda sekundärer → Strong (4), en → Good
                        // (3), ingen → Basic (1). (1+sekundärer-aritmetiken duger inte längre när
                        // Related sitter på 2 — boolean-mappning i stället.)
                        : (regions.Contains(EF.Property<string?>(j, RegionColumn))
                                || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn)))
                            && employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))
                            ? 4
                            : (regions.Contains(EF.Property<string?>(j, RegionColumn))
                                    || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn)))
                                || employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))
                                ? 3
                                : 1;

    // Komponerar ett EF-översättbart predikat `selectedRanks.Contains(rank(j))` genom att
    // ÅTERANVÄNDA exakt samma rank-Body + parameter (ingen duplikat-logik, ingen LINQKit).
    // EF översätter int[].Contains(<CASE-uttryck>) → `<CASE> = ANY(@ranks)`.
    private static Expression<Func<JobAd, bool>> RankInSet(
        Expression<Func<JobAd, int>> rank, int[] selectedRanks)
    {
        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(int)],
            Expression.Constant(selectedRanks),
            rank.Body);
        return Expression.Lambda<Func<JobAd, bool>>(contains, rank.Parameters[0]);
    }

    // Fast-bandets grad → rank-heltal (parity GradeRankExpression). #300 PR-4 (ADR 0084 §F2):
    // Related infogad mellan Basic och Good → Good/Strong omnumrerade upp (Basic=1, Related=2,
    // Good=3, Strong=4). MÅSTE förbli identisk med GradeRankExpression:s heltal (DRY — annars
    // väljer grad-WHERE fel rank-hink). Top är inte Fast-beräkningsbar (G3-OPT-A) och avvisas av
    // ListJobAdsQueryValidator wire-side; skulle den ändå nå hit är det ett programmeringsfel →
    // fail-fast (parity ApplySort).
    private static int GradeToRank(MatchGrade grade) => grade switch
    {
        MatchGrade.Basic => 1,
        MatchGrade.Related => 2,
        MatchGrade.Good => 3,
        MatchGrade.Strong => 4,
        _ => throw new ArgumentOutOfRangeException(
            nameof(grade), grade,
            "Endast Grund/Relaterat/Bra/Stark är filtrerbara (Fast-bandet) — validatorn ska ha avvisat Topp."),
    };
}
