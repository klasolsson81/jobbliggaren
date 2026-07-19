using System.Linq.Expressions;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
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
/// <c>MatchScorer.ScoreSsykMembership</c>/<c>ScoreEmploymentMembership</c> (yrke/anställning) + <c>MatchScorer.ScoreOrtUnion</c>
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
    // facett-kolumner (EF.Property-nycklar) — parity MatchScorer; kolumn-
    // namnen är en Infrastructure-hemlighet som aldrig läcker till Application.
    private const string OccupationGroupColumn = "OccupationGroupConceptId";
    private const string RegionColumn = "RegionConceptId";
    private const string EmploymentTypeColumn = "EmploymentTypeConceptId";

    // Spår 3 (ADR 0076-amendment 2026-06-21) — kommun-granulariteten i ort-dimensionen.
    // Sort-ranken speglar nu MatchScorer.ScoreOrtUnion (region ∪ municipality) i stället
    // för region-only membership-scoring. STORED generated från
    // raw_payload->'workplace_address'->>'municipality_concept_id' (parity scorern).
    private const string MunicipalityColumn = "MunicipalityConceptId";

    // #551 (ADR 0076 #551 amendment) — AF:s remote/distans-flagga, en `bool NOT NULL`-kolumn.
    // SQL-tvillingen till MatchScorer.ScoreOrtUnions remote-override: en remote-annons golvas ALDRIG
    // på ort och räknas som en bekräftad sekundär (för en angiven-ort-användare). Läses som non-null
    // bool → grenen förblir TVÅVÄRD (ingen trevärd-NULL-fälla, till skillnad från region/kommun).
    private const string RemoteColumn = "Remote";

    // STORED generated jsonb companion (extracted_lexemes = Lexeme-array, GIN-indexerad)
    // — bär skill-concept-ids (Lexeme == ConceptId för Skill/Requirement-termer). F4-15:s
    // gyllene rung testar `extracted_lexemes ?| @cvSkillIds` (EF.Functions.JsonExistAny).
    private const string ExtractedLexemesColumn = "ExtractedLexemes";

    // #452 — STORED generated org.nr shadow column (ADR 0087 D1), the GROUP key for the per-employer
    // matching-count. Column name is an Infrastructure secret (parity the columns above); it never
    // leaks to Application. org.nr is read server-side as the GROUP key ONLY (never surfaced/logged).
    private const string OrganizationNumberColumn = "OrganizationNumber";

    public async ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
        JobAdFilterCriteria filter,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        JobAdSortBy sort,
        bool orderByMatchRank,
        JobAdStatusFilter status,
        JobSeekerId seekerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grades);

        // Profil-listorna fångas lokalt → EF binder dem som parametrar (= ANY).
        // SSYK är icke-tom (handlerns gate). regions/employment kan vara tomma
        // (NotAssessed — varken bekräftar eller golvar; vacuous-gate-doktrinen, oförändrad
        // av #552-grinden som enbart gäller ANGIVEN pref mot NULL-shadow).
        var fast = profile.Fast;
        var ssyk = fast.SsykGroupConceptIds;
        // #300 PR-4 (ADR 0084 §F4): the RELATED ssyk-4 set (substitutable occupations). Populated
        // when the live ?relaterade=on toggle is on (off by default); with it off the Related
        // branch in GradeRankExpression is inert (every ad either gates out or scores via the
        // exact set, byte-for-byte the pre-#300 result).
        var relatedSsyk = fast.RelatedSsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        // #477 Low 1 — föräldra-länen för de föredragna kommunerna (kommun→län-containment).
        // Fylls ovillkorligt av MatchProfileBuilder; tom när ingen kommun vald → containment-
        // disjunkten i GradeRankExpression blir konstant false → pre-#477 byte-for-byte.
        var containmentRegions = fast.ContainmentRegionConceptIds;
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

        // #383 — den per-användar-status-predikaten (sparade/ansökta/dölj ansökta)
        // EXISTS-stackas ovanpå filter-SPOT:en FÖRE grad-WHERE/count (count-korrekthet —
        // counten nedan måste räknas över den status-filtrerade mängden). No-op när
        // status är inaktiv (match-only-vägen passerar JobAdStatusFilter.None) → byte-for-
        // byte som förr. Soft-delete på Application ärvs automatiskt via EXISTS-subqueryn.
        baseQuery = ApplyStatusFilter(baseQuery, status, seekerId);

        // EN delad rank-Expression (DRY) — speglar MatchGradeCalculator.Grade(MatchScore).
        // Konsumeras av grad-WHERE, count OCH match-rank-ORDER BY så filter och sort aldrig
        // divergerar (CTO-re-bind 2026-06-23). Oracle-pinnad.
        var rankExpr = GradeRankExpression(
            ssyk, relatedSsyk, regions, municipalities, containmentRegions, employment, ortStated, employmentStated);

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

        // Count-korrekthet (CTO-re-bind rad-86-fix + #383): när grad-WHERE ELLER status-
        // filtret är aktivt MÅSTE total-antalet räknas om över den (grad/status-)filtrerade
        // mängden (`graded` bär redan status-EXISTS:en från baseQuery) — annars överräknar
        // den delade anonyma port-counten → spök-paginering. Separat count-query (CLAUDE
        // §3.6). Varken grad eller status aktiv ⇒ port-counten är exakt rätt (och bär
        // TD-94-bitmap-plan-hygienen).
        // #744 — den grad/status-filtrerade counten bär filter.Q (nås via
        // ListJobAdsQueryHandler med fritext) → samma TOAST-detoast-seqscan som den delade
        // port-counten fixade. Gata bitmap-plan-hygienen på q (no-op utan fritext). Den
        // andra grenen delegerar till den delade port-counten, som self-gatar efter #744.
        var totalCount = grades.Count > 0 || status.IsActive
            ? await BitmapPlanCount.CountWithBitmapPlanAsync(
                db, JobAdSearchComposition.HasFreeTextQuery(filter.Q),
                graded.CountAsync, cancellationToken)
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
        // annars divergerar count från list-vägens TotalCount (spök-paginering). relatedSsyk
        // fylls bara när den live ?relaterade=on-toggeln är på (av som standard).
        var relatedSsyk = fast.RelatedSsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        // #477 Low 1 — föräldra-länen för de föredragna kommunerna (kommun→län-containment).
        // Fylls ovillkorligt av MatchProfileBuilder; tom när ingen kommun vald → containment-
        // disjunkten i GradeRankExpression blir konstant false → pre-#477 byte-for-byte.
        var containmentRegions = fast.ContainmentRegionConceptIds;
        var employment = fast.PreferredEmploymentTypeConceptIds;
        var ortStated = regions.Count > 0 || municipalities.Count > 0;
        var employmentStated = employment.Count > 0;

        // SAMMA filter-SPOT + SAMMA delade GradeRankExpression som SearchPerUserAsync:s
        // grad-WHERE → counten är per konstruktion lika med list-vägens TotalCount för
        // samma profil + grad-set (siffra↔landning-koherens, oracle-pinnad). Separat
        // count-query (CLAUDE §3.6): ingen sort, paginering eller projektion.
        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        // #744 — routas genom den gatade bitmap-plan-hygienen som list-vägen. Notis-vägen
        // (STEG 6) skickar alltid NoFilter med Q==null → HasFreeTextQuery false → bare
        // count, byte-for-byte som förr. Att routa ändå (i st.f. ett medvetet "skippar
        // hygien"-special-case) tar bort asymmetrin och gör en framtida q-bärande anropare
        // automatiskt korrekt i st.f. att tyst åter-exponera TD-94:s detoast-seqscan.
        var useHygiene = JobAdSearchComposition.HasFreeTextQuery(filter.Q);

        if (grades.Count == 0)
            return await BitmapPlanCount.CountWithBitmapPlanAsync(
                db, useHygiene, baseQuery.CountAsync, cancellationToken);

        var rankExpr = GradeRankExpression(
            ssyk, relatedSsyk, regions, municipalities, containmentRegions, employment, ortStated, employmentStated);
        var selectedRanks = grades.Select(GradeToRank).Distinct().ToArray();
        var gradedQuery = baseQuery.Where(RankInSet(rankExpr, selectedRanks));
        return await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, useHygiene, gradedQuery.CountAsync, cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<string, int>> CountPerUserByEmployerAsync(
        IReadOnlyList<string> organizationNumbers,
        FullCandidateMatchProfile profile,
        IReadOnlyList<MatchGrade> grades,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizationNumbers);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(grades);

        // No watched employers / no grade band → nothing to count (the handler already gates the
        // no-SSYK case, but stay defensive: an empty selectedRanks would translate to `= ANY('{}')`
        // and return nothing anyway).
        if (organizationNumbers.Count == 0 || grades.Count == 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        var fast = profile.Fast;
        var ssyk = fast.SsykGroupConceptIds;
        var relatedSsyk = fast.RelatedSsykGroupConceptIds;
        var regions = fast.PreferredRegionConceptIds;
        var municipalities = fast.PreferredMunicipalityConceptIds;
        // #477 Low 1 — föräldra-länen för de föredragna kommunerna (kommun→län-containment).
        // Fylls ovillkorligt av MatchProfileBuilder; tom när ingen kommun vald → containment-
        // disjunkten i GradeRankExpression blir konstant false → pre-#477 byte-for-byte.
        var containmentRegions = fast.ContainmentRegionConceptIds;
        var employment = fast.PreferredEmploymentTypeConceptIds;
        var ortStated = regions.Count > 0 || municipalities.Count > 0;
        var employmentStated = employment.Count > 0;

        // Captured locals → EF binds them as parameters (= ANY(...)). string? element type so the
        // EF.Property<string?> Contains translates cleanly (the org.nr shadow column is nullable; a
        // NULL-org.nr ad never matches). Values themselves non-null.
        var orgNrs = organizationNumbers.Select(o => (string?)o).ToList();

        // SAME shared GradeRankExpression SSOT as SearchPerUserAsync/CountPerUserAsync — so the hub
        // count can never diverge from /jobb's grade for the same ad (ADR 0079). Grade-WHERE via the
        // same RankInSet helper (positive-only: rank 0 excluded once a grade is selected).
        var rankExpr = GradeRankExpression(
            ssyk, relatedSsyk, regions, municipalities, containmentRegions, employment, ortStated, employmentStated);
        var selectedRanks = grades.Select(GradeToRank).Distinct().ToArray();

        // Bounded per-employer GROUP BY over PUBLIC Active job_ads (parity #447 ActiveAdCount +
        // the CountPerUserAsync grade-WHERE). The Status == Active predicate below IS the whole
        // exclusion: JobAd has no soft-delete axis and no query filter (#821 — and a hand-rolled
        // deleted_at predicate was always forbidden, ADR 0048). org.nr is the GROUP key only, server-side.
        // Only Postgres computes the generated shadow columns + translates GradeRankExpression +
        // the GROUP BY, so the count is proven by the Testcontainers oracle (InMemory hides all).
        var counts = await db.JobAds
            .AsNoTracking()
            .Where(j => orgNrs.Contains(EF.Property<string?>(j, OrganizationNumberColumn))
                        && j.Status == JobAdStatus.Active)
            .Where(RankInSet(rankExpr, selectedRanks))
            .GroupBy(j => EF.Property<string?>(j, OrganizationNumberColumn))
            .Select(g => new { OrgNr = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts
            .Where(x => x.OrgNr is not null)
            .ToDictionary(x => x.OrgNr!, x => x.Count, StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlySet<JobAdId>> FilterToMatchingAsync(
        FullCandidateMatchProfile profile,
        IReadOnlyCollection<JobAdId> jobAdIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(jobAdIds);

        // Fail-fast (parity SearchByStatusAsync's active-status throw): an empty-SSYK profile
        // grades every ad 0 — the empty result would MEAN "not assessable", not "zero matches"
        // (the dishonest-0 trap). The caller branches on assessability BEFORE calling (a
        // profile-less user's "endast matchade" filter is inert — RF-5 under-fork (i)).
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            throw new ArgumentException(
                "FilterToMatchingAsync kräver en bedömbar profil (minst en angiven yrkesgrupp); "
                + "för en profil-lös användare är \"endast matchade\"-filtret inert och metoden "
                + "ska inte anropas.",
                nameof(profile));

        if (jobAdIds.Count == 0)
            return new HashSet<JobAdId>();

        var fast = profile.Fast;
        var ortStated = fast.PreferredRegionConceptIds.Count > 0
                        || fast.PreferredMunicipalityConceptIds.Count > 0;
        var employmentStated = fast.PreferredEmploymentTypeConceptIds.Count > 0;

        // SAME shared GradeRankExpression SSOT as SearchPerUserAsync/CountPerUser* (ADR 0079);
        // the ≥Good floor is the FIXED Fast band {Good, Strong} (RF-5=5A — exact: Fast ≡ Full at
        // ≥Good; Top never lifts an ad ACROSS the Good threshold, only within the band).
        var rankExpr = GradeRankExpression(
            fast.SsykGroupConceptIds,
            fast.RelatedSsykGroupConceptIds,
            fast.PreferredRegionConceptIds,
            fast.PreferredMunicipalityConceptIds,
            fast.ContainmentRegionConceptIds,
            fast.PreferredEmploymentTypeConceptIds,
            ortStated,
            employmentStated);
        var goodOrBetterRanks = new[] { GradeToRank(MatchGrade.Good), GradeToRank(MatchGrade.Strong) };

        // ONE round-trip via parameterized `= ANY` (the canonical strongly-typed-id-set pattern
        // from MatchScorer.ScoreBatchAsync — Contains() over JobAdId does not translate;
        // FromSql parameterizes the Guid[], injection-safe, CLAUDE.md §5). JobAd carries no query
        // filter (#821) — the explicit Status gate below is the exclusion, parity
        // CountPerUserByEmployerAsync.
        var ids = jobAdIds.Select(id => id.Value).Distinct().ToArray();

        var matching = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(RankInSet(rankExpr, goodOrBetterRanks))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        return matching.ToHashSet();
    }

    public async ValueTask<PagedResult<JobAdDto>> SearchByStatusAsync(
        JobAdFilterCriteria filter,
        JobSeekerId seekerId,
        JobAdStatusFilter status,
        JobAdSortBy sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        // Fail-fast (CLAUDE §3.4 — dotnet-architect Minor): denna väg är ENBART för
        // ett aktivt status-filter (handlern gatar den bakom status.IsActive). Slank
        // JobAdStatusFilter.None in skulle ApplyStatusFilter no-op:a och porten tyst
        // degenerera till "lista allt med count-omräkning" — synliggör felet i stället.
        if (!status.IsActive)
            throw new ArgumentException(
                "SearchByStatusAsync kräver ett aktivt status-filter; använd den anonyma "
                + "sökvägen när inget status är valt.",
                nameof(status));

        // Status-only (#383): återanvänder EXAKT samma filter-SPOT + rena sort som
        // default-vägen — ingen profil, ingen grad-rank, ingen match-ordning (frikopplad
        // från SSYK-match-gaten, SRP). Bara status-EXISTS:en läggs på.
        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);
        var filtered = ApplyStatusFilter(baseQuery, status, seekerId);

        // Count-korrekthet: över den status-filtrerade mängden, aldrig den anonyma
        // port-counten (annars spök-paginering). Separat count-query (CLAUDE §3.6).
        // #744 — filtered bär filter.Q (status-only-vägen nås via ListJobAdsQueryHandler
        // med fritext) → samma detoast-hål som den grad-filtrerade counten. Samma gatade
        // bitmap-plan-hygien (no-op utan fritext).
        var totalCount = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, JobAdSearchComposition.HasFreeTextQuery(filter.Q),
            filtered.CountAsync, cancellationToken);

        var items = await JobAdSearchComposition.ApplySort(filtered, sort, filter.Q)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(JobAdSearchComposition.ToDto())
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, page, pageSize);
    }

    // #383 (CTO-bind cto-7f3a9c2e1b4d8a6f, Approach B) — den delade status-WHERE:n
    // (EXISTS/NOT EXISTS), använd av BÅDE SearchPerUserAsync (status+match) och
    // SearchByStatusAsync (status-only). EXISTS (VO==VO-kolumnjämförelse `s.JobAdId == j.Id`),
    // INTE IN (`List<VO>.Contains` 500:ar i översättningen — ef_strongly_typed_vo_contains).
    // No-op när status är inaktiv (match-only-vägen) → seekerId oanvänd.
    //
    // savedOnly ∨ appliedOnly = UNION (OR — samma som status-batchen, ADR 0063); hideApplied
    // = NOT EXISTS (ansökt), giltig tillsammans med savedOnly men ömsesidigt uteslutande mot
    // appliedOnly (validatorn 400:ar). SavedJobAd är hard-delete (rad finns iff sparad nu);
    // Application bär soft-delete-HasQueryFilter (DeletedAt == null) som ärvs automatiskt in
    // i EXISTS-subqueryn — och kort-badgen (GetJobAdStatusBatchQueryHandler) läser db.Applications
    // med SAMMA globala filter, så badge↔filter förblir koherenta ÄVEN för en raderad ansökan
    // (båda exkluderar den). Koherensen vilar alltså på det delade HasQueryFilter, inte på en
    // slump — håll ihop dem om filtret någonsin flyttas.
    //
    // "Ansökt" = vilken som helst (ej soft-deletad) Application-rad mot annonsen — INGEN
    // ApplicationStatus-gallring. Detta är MEDVETET koherent med kort-badgen
    // (GetJobAdStatusBatchQueryHandler, ADR 0063), som taggar "Ansökt" på exakt samma
    // any-application-villkor: filtret "Ansökta"/"Dölj ansökta" MÅSTE spegla badgen (annars
    // visar ett kort "Ansökt" men göms/visas inte av filtret — drift, jfr badge↔status-
    // synk-disciplinen). Skulle "Ansökt" senare omdefinieras till submittad (AppliedAt != null)
    // ändras BÅDE badgen och detta filter tillsammans (ADR 0063-amendment), aldrig bara ett.
    private IQueryable<JobAd> ApplyStatusFilter(
        IQueryable<JobAd> source, JobAdStatusFilter status, JobSeekerId seekerId)
    {
        if (!status.IsActive)
            return source;

        // Lokal kopia → EF binder seekern som en parameter (parity profil-listorna).
        var seeker = seekerId;
        var query = source;

        if (status.SavedOnly && status.AppliedOnly)
        {
            // Union (OR): annonser jag sparat ELLER sökt.
            query = query.Where(j =>
                db.SavedJobAds.Any(s => s.JobSeekerId == seeker && s.JobAdId == j.Id)
                || db.Applications.Any(a => a.JobSeekerId == seeker && a.JobAdId == j.Id));
        }
        else if (status.SavedOnly)
        {
            query = query.Where(j =>
                db.SavedJobAds.Any(s => s.JobSeekerId == seeker && s.JobAdId == j.Id));
        }
        else if (status.AppliedOnly)
        {
            query = query.Where(j =>
                db.Applications.Any(a => a.JobSeekerId == seeker && a.JobAdId == j.Id));
        }

        if (status.HideApplied)
        {
            // NOT EXISTS — dölj annonser jag redan sökt (giltig med savedOnly; mutex mot
            // appliedOnly, validator-grindad).
            query = query.Where(j =>
                !db.Applications.Any(a => a.JobSeekerId == seeker && a.JobAdId == j.Id));
        }

        return query;
    }

    // Den delade grad-rank-Expression:en (SSOT) — en kompilerad spegel av den Fast
    // MatchGradeCalculator.Grade(MatchScore, isRelated)-stegen över shadow-kolumnerna.
    // #300 PR-4 (ADR 0084 §F2/§F4): Related infogad MELLAN Basic och Good → rank-omnumrering.
    // Returnerar 0 (otaggad/SSYK ∉ exact ∪ related), 1 (Basic — golv vid motsägande ort/
    // anställning ELLER inga bekräftade sekundärer), 2 (Related — yrket är en SUBSTITUERBAR
    // granne, ej i den angivna exakta mängden; platt cap), 3 (Good — en bekräftad sekundär),
    // 4 (Strong — båda). Gren-ordningen speglar calculatorns cap-ordning EXAKT: gate →
    // Related-cap (FÖRE RB1) → RB1-golv → sekundärer. En related-only-annons i fel stad läser
    // därför Related (2), ALDRIG Basic (1). IMPL-TRAP (CTO C, #552-uppdaterad): en NULL-shadow
    // på en ANGIVEN dimension GOLVAR numera (grinden, ADR 0076-amendment) — men ALLTID via en
    // EXPLICIT `== null`-disjunkt, aldrig ett naket !list.Contains(col) (trevärd logik: NOT
    // (col = ANY(...)) är NULL, ej TRUE, för NULL-kolumn — golvet skulle tyst utebli i rå SQL).
    // relatedSsyk fylls när den live ?relaterade=on-toggeln är på (av som
    // standard); med den av är Related-grenen inert och exact-grenarna byte-for-byte som pre-#300
    // (bara rank-heltalen omnumrerade; GradeToRank
    // omnumreras likadant så filter/sort/count förblir koherenta — oracle-pinnat).
    private static Expression<Func<JobAd, int>> GradeRankExpression(
        IReadOnlyList<string> ssyk,
        IReadOnlyList<string> relatedSsyk,
        IReadOnlyList<string> regions,
        IReadOnlyList<string> municipalities,
        IReadOnlyList<string> containmentRegions,
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
                    // #552-grinden (ADR 0076-amendment): en ANGIVEN ort/anställningsform mot en
                    // annons vars shadow är NULL golvar OCKSÅ — via en EXPLICIT `== null`-disjunkt
                    // (speglar ScoreOrtUnion/ScoreEmploymentMembership NoMatch-med-tom-evidens).
                    // TREVÄRD-LOGIK-FÄLLAN: disjunkten får ALDRIG uttryckas som ett naket
                    // !list.Contains(nullCol) — `NOT (col = ANY(...))` är NULL (ej TRUE) för en
                    // NULL-kolumn i rå SQL; endast den explicita IS NULL-grenen är bevisbart
                    // golvande oavsett null-semantik-regim. Testcontainers-oraklen pinnar detta.
                    : ((ortStated
                            // #551 — en remote-annons golvas ALDRIG på ort (speglar ScoreOrtUnions
                            // remote-override som returnerar Match FÖRE both-NULL-#552-grinden). `!remote`
                            // som en TVÅVÄRD term (bool NOT NULL) → ingen trevärd-fälla. För en icke-remote
                            // annons är !remote = true → grenen byte-identisk med pre-#551.
                            && !EF.Property<bool>(j, RemoteColumn)
                            && ((EF.Property<string?>(j, RegionColumn) == null
                                    && EF.Property<string?>(j, MunicipalityColumn) == null)
                                || ((EF.Property<string?>(j, RegionColumn) != null
                                        || EF.Property<string?>(j, MunicipalityColumn) != null)
                                    && !(regions.Contains(EF.Property<string?>(j, RegionColumn))
                                        || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))
                                        // #477 Low 1 — en LÄN-ONLY-annons (municipality NULL) vars län
                                        // INNEHÅLLER en föredragen kommun är INGEN ort-motsägelse (speglar
                                        // ScoreOrtUnions containment-gren → NotAssessed, ej NoMatch). Muni
                                        // NULL-grinden håller grenen till län-only: en kommun-specifik annons
                                        // i en icke-föredragen kommun i samma län golvas fortsatt (RB1).
                                        || (EF.Property<string?>(j, MunicipalityColumn) == null
                                            && containmentRegions.Contains(EF.Property<string?>(j, RegionColumn)))))))
                        || (employmentStated
                            && (EF.Property<string?>(j, EmploymentTypeColumn) == null
                                || !employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn)))))
                        ? 1
                        // Exakt träff, ingen motsägelse: båda sekundärer → Strong (4), en → Good
                        // (3), ingen → Basic (1). (1+sekundärer-aritmetiken duger inte längre när
                        // Related sitter på 2 — boolean-mappning i stället.)
                        // #551 — remote räknas som en bekräftad ort-sekundär, men BARA för en
                        // angiven-ort-användare (`ortStated && remote`) — speglar ScoreOrtUnion, där
                        // remote-override:n bara fyrar efter `!stated`-returen. En icke-remote annons:
                        // `ortStated && false` = false → disjunkten byte-identisk med pre-#551.
                        : (regions.Contains(EF.Property<string?>(j, RegionColumn))
                                || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))
                                || (ortStated && EF.Property<bool>(j, RemoteColumn)))
                            && employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))
                            ? 4
                            // #551 — samma remote-sekundär-disjunkt som Strong-grenen ovan (en bekräftad
                            // ort-sekundär för angiven-ort-användare); icke-remote = byte-identisk.
                            : (regions.Contains(EF.Property<string?>(j, RegionColumn))
                                    || municipalities.Contains(EF.Property<string?>(j, MunicipalityColumn))
                                    || (ortStated && EF.Property<bool>(j, RemoteColumn)))
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
