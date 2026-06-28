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
/// <item>0 = otaggad (SSYK ej Match) → exkluderas av grad-filtret (positiv-only),
/// sorteras sist i match-rank;</item>
/// <item>1 = Basic (SSYK Match, men en angiven ort/anställningsform motsäger — golvar);</item>
/// <item>1 + antal bekräftade sekundärer (ort (region∪kommun) / anställningsform) =
/// 1/2/3 (Basic/Good/Strong). En <c>NotAssessed</c>-dimension varken bekräftar eller golvar.</item>
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
        // konstant 0 (EF prunes).
        var cvSkillIds = profile.CvSkillConceptIds.ToArray();
        var skillStated = cvSkillIds.Length > 0;

        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        // EN delad rank-Expression (DRY) — speglar MatchGradeCalculator.Grade(MatchScore).
        // Konsumeras av grad-WHERE, count OCH match-rank-ORDER BY så filter och sort aldrig
        // divergerar (CTO-re-bind 2026-06-23). Oracle-pinnad.
        var rankExpr = GradeRankExpression(
            ssyk, regions, municipalities, employment, ortStated, employmentStated);

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
                // Grad-rank fallande (3=Strong … 0=otaggad sist) — samma delade Expression
                // som grad-WHERE/count (DRY, oracle-pinnad).
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
            ssyk, regions, municipalities, employment, ortStated, employmentStated);
        var selectedRanks = grades.Select(GradeToRank).Distinct().ToArray();
        return await baseQuery
            .Where(RankInSet(rankExpr, selectedRanks))
            .CountAsync(cancellationToken);
    }

    // Den delade grad-rank-Expression:en (SSOT) — en kompilerad spegel av den Fast
    // MatchGradeCalculator.Grade(MatchScore)-stegen över shadow-kolumnerna. Returnerar
    // 0 (otaggad/SSYK ej Match), 1 (Basic — golv vid motsägande ort/anställning), 2
    // (Good — en bekräftad sekundär), 3 (Strong — båda). IMPL-TRAP (CTO C): motsägelse-
    // golvet är ett KOMBINERAT predikat (angiven preferens OCH annonsen har ett ort-/
    // anställnings-värde OCH ingen union-träff) — aldrig ett naket !list.Contains(col),
    // som skulle läsa en NULL-shadow som "inte i listan".
    private static Expression<Func<JobAd, int>> GradeRankExpression(
        IReadOnlyList<string> ssyk,
        IReadOnlyList<string> regions,
        IReadOnlyList<string> municipalities,
        IReadOnlyList<string> employment,
        bool ortStated,
        bool employmentStated) =>
        j =>
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
                        + (employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn)) ? 1 : 0);

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

    // Fast-bandets grad → rank-heltal (parity GradeRankExpression). Top är inte
    // Fast-beräkningsbar (G3-OPT-A) och avvisas av ListJobAdsQueryValidator wire-side;
    // skulle den ändå nå hit är det ett programmeringsfel → fail-fast (parity ApplySort).
    private static int GradeToRank(MatchGrade grade) => grade switch
    {
        MatchGrade.Basic => 1,
        MatchGrade.Good => 2,
        MatchGrade.Strong => 3,
        _ => throw new ArgumentOutOfRangeException(
            nameof(grade), grade,
            "Endast Grund/Bra/Stark är filtrerbara (Fast-bandet) — validatorn ska ha avvisat Topp."),
    };
}
