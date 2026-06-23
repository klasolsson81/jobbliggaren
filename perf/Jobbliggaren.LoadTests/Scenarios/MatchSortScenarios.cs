// Jobbliggaren perf fitness function — GET /api/v1/job-ads?sortBy=MatchDesc (F4-14/F4-15).
//
// ADR 0076 Decision 4/5/7 + ADR 0045 Beslut 1 klass (a) read-query/list:
//   p95 ≤ 300 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//   p99 600 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT-PROFIL (F4-14/F4-15 — uppdaterad med F4-15 CV-read-delta):
//   GET /api/v1/job-ads?sortBy=5 (ListJobAdsSort.MatchDesc = 5)
//   Auth-gated (RequireAuthorization via grupp, ADR 0005). LOADTEST_BEARER_TOKEN
//   krävs — saknas den → 401 → fail-count → BudgetReporter-warning (signalen
//   mäter INTE match-sort-hot-path).
//
//   Handler ListJobAdsQueryHandler (F4-15, bygger på F4-14):
//     1. IMatchProfileBuilder.BuildFullForSortAsync (NY F4-15):
//        - 1 SELECT JobSeekers (AsNoTracking) → JobSeeker + MatchPreferences.
//        - 1 SELECT Resumes WHERE id = PrimaryResumeId (AsNoTracking, NO Include(Versions))
//          → Resume.TopSkills (plaintext text[], ADR 0058/0059, NO DEK).
//        - In-memory ISkillResolver.Resolve(TopSkills[≤5]) → IReadOnlySet<string> concept-ids.
//        → FullCandidateMatchProfile (fast + top-5 skill concept-ids).
//        Deltakostnad vs F4-14: +1 SELECT Resumes + in-memory resolver (sub-ms).
//        INGEN DEK-uppvärmning (plaintext projection). INGEN interceptor-dekryptering.
//     2. SSYK-gate (Decision 7 honest fallback): tom ssyk → fallback till
//        PublishedAtDesc-sort (INGEN match-sort-overhead; IJobAdSearchQuery).
//        NÄR SSYK är angiven (testets förväntade case — kräver autentiserad
//        användare MED angiven yrkespreferens):
//     3. IMatchSortedJobAdSearchQuery.SearchByMatchAsync:
//        - CountAsync via IJobAdSearchQuery (re-use): 1 transaktionell COUNT
//          med SET LOCAL enable_seqscan=off (TD-94 bitmap-plan-tvång).
//        - ApplyFilter (JobAdSearchComposition.ApplyFilter) → WHERE status='Active'
//          (+ eventuella filter).
//        - ORDER BY (F4-15 GOLDEN RUNG NY):
//            CASE WHEN rank=3 AND extracted_lexemes ?| @cvConceptIds THEN 4 ELSE rank END DESC,
//            published_at DESC, id ASC.
//          GIN-index (ix_job_ads_extracted_lexemes_gin, ADR 0075) serverar ?| predikatet;
//          rank-CASE forblir NON-SARGABLE (parity F4-14). Postgres beräknar rank + GIN-check
//          per-rad på hela filtrerade mängden → Seq Scan + Sort.
//          Deltakostnad vs F4-14: GIN ?| per rad tillkommer, men predikatet körs AFTER
//          rank-beräkning (GIN ?| på jsonb är fast per rad, inte en GIN Index Scan i ORDER BY).
//        - Skip/Take (paginering).
//        - Select → JobAdDto.
//
//   Tre test-dimensioner (svarar mot "worst case", "typical case", "fallback"):
//     (A) WorstCase: sortBy=MatchDesc + INGA filter + ANGIVEN preferens + PRIMARY CV
//         MED TopSkills. Postgres måste scanna + sortera hela aktiva korpusen
//         (~54k rader) med rank-CASE + GIN ?| per rad. Det är den maximala
//         planner-kostnaden för match-sorten inkl. F4-15 golden-rung-delta.
//         Kräver: LOADTEST_BEARER_TOKEN (JWT med preferens + primary CV med TopSkills).
//     (B) Typical: sortBy=MatchDesc + EN occupationGroup-filter. Filter reducerar
//         mängden; rank-CASE + GIN ?| beräknas bara på träffmängden. Mer typisk
//         prod-körning.
//     (C) FallbackNoProfile: sortBy=MatchDesc men användaren har INGEN angiven
//         yrkespreferens (tom SSYK-gate → handler faller tillbaka till
//         PublishedAtDesc-sort via IJobAdSearchQuery.SearchAsync). I F4-15 bygger
//         BuildFullForSortAsync ändå SSYK-check (1 SELECT JobSeekers);
//         om PrimaryResumeId finns → 1 SELECT Resumes; sedan SSYK-gate → fallback.
//         Mäter: F4-15:s BUILD-overhead (2 SELECT:s) + default-sort-kostnaden.
//
// ─────────────────────────────────────────────────────────────────────────────
// F4-15 MÄTKRAV (CTO R5 / CLAUDE.md §2.5):
//   Det autentiserade skill-bearing-profil-kravet är OFÖRÄNDRAT vs F4-14 — men nu
//   KRÄVER en MENINGSFULL worst-case-mätning att testanvändaren har:
//     (1) Angiven yrkespreferens (SsykGroupConceptIds icke-tom) — aktiverar match-sort.
//     (2) Primary CV med ≥1 TopSkill (Resume.TopSkills icke-tom) — aktiverar golden rung.
//   Utan (1): FallbackNoProfile-vägen (SSYK-gate → default-sort).
//   Utan (2): Profilen byggs utan skill concept-ids → golden rung aktiveras ej (GIN ?|
//             på en tom array returnerar false per-rad → ingen golden increment).
//   WorstCase + Typical MED (1)+(2) = FULL F4-15 golden-rung-träff-mätning.
//   WorstCase/Typical UTAN (2) = F4-14-likvärdig signal + +1 SELECT Resumes overhead.
//
// ─────────────────────────────────────────────────────────────────────────────
// AUTH-KRAV (kritisk för meningsfull mätning):
//   Alla tre scenarierna kräver LOADTEST_BEARER_TOKEN. Utan token → 401 → fail-count.
//   Scenario (A) och (B) kräver dessutom att den autentiserade användaren har en
//   MatchPreferences-post med minst en angiven yrkespreferens (SsykGroupConceptIds
//   icke-tom). Saknas den → SSYK-gate aktiv → fallback-sort (scenario (C):s bana).
//   Scenario (C) mäter fallback-banan explicit — använd en testanvändare UTAN
//   angiven yrkespreferens, ELLER utelämna LOADTEST_OCCUPATION_GROUP_ID (se nedan).
//
//   Token-källans wiring görs i körnings-miljön (CI eller lokal dev-test-konto,
//   se docs/runbooks/frontend-visual-verification.md cred-path), inte här.
//
// ─────────────────────────────────────────────────────────────────────────────
// ENV-VARIABLER (sätts av CI-job eller lokal kalibrering):
//   LOADTEST_BEARER_TOKEN   — JWT för autentiserad mätning. Krävs för alla scenarier.
//   LOADTEST_OCCUPATION_GROUP_ID — JobTech occupation-group concept-id (typisk prod-
//                                   filter, t.ex. "iZge_5CT_Ahu" = Systemutvecklare).
//                                   Används av Typical-scenariot. Utelämnas → scenariot
//                                   kör utan filter (samma väg som WorstCase).
//                                   Välj ett id som finns i din loadtest-DB.
//
// ─────────────────────────────────────────────────────────────────────────────
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, ej gissning):
//   ListReadPolicy 60/min per UserId (claim "sub"). Alla tre scenarierna delar
//   samma user-bucket om de körs parallellt med "all"-selector + samma token.
//   Summering av rates vid parallell körning av alla tre scenarierna:
//     WorstCase:      0,25 RPS = 15 req/min
//     Typical:        0,25 RPS = 15 req/min
//     FallbackNoProfile: 0,25 RPS = 15 req/min
//     Total:          0,75 RPS = 45 req/min = 75 % av 60/min-taket
//   Headroom mot rate-limit-kollision: 15 req/min att dela med övriga
//   "all"-scenarier (q-count, facet-counts, match-tags, etc.).
//
//   Lägre RPS (0,25/s) än andra klass (a)-scenarier motiveras av:
//     - WorstCase-queryn är väsentligt tyngre (Seq Scan + Sort på ~54k rader +
//       COUNT-round-trip) än t.ex. landing-stats (Redis-cache-hit).
//     - "all"-selectorn delar ListRead-bucketen med q-count (0,5 RPS) och
//       facet-counts (1 RPS). Match-sort-scenarierna håller sig strikt under taket.
//     - 0,25 RPS × 120s = 30 samples → statistisk nedre gräns för observe-only
//       trend (Tukey: n=30 räcker med brett konfidensintervall Fas 1).
//
//   Isolerad körning med LOADTEST_SCENARIOS=match-sort kan höja till 0,5 RPS
//   (30 req/min = 50 % av taket) för bättre p95-granularitet (60 samples).
//
// ─────────────────────────────────────────────────────────────────────────────
// PLAN-SHAPE-RESONEMANG (ersätter EXPLAIN pga frånvaro av loadtest-DB):
//   Match-sort körs som NON-SARGABLE rank-CASE i ORDER BY. Det innebär att
//   Postgres ALLTID måste:
//     1. Scanna hela aktiva mängden (WHERE status='Active') — ett befintligt
//        partiellt GIN-index (ix_job_ads_search_vector WHERE status='Active')
//        kan INTE serva en ORDER BY-rank. Planner väljer Seq Scan / Bitmap Heap
//        på status-predikatet.
//     2. Beräkna rank-CASE per rad.
//     3. Sortera resultatet (Sort node med kostnaden O(n log n)).
//     4. Skip/Take (Limit node).
//   Befintliga shadow-kolumn-index (ix_job_ads_occupation_group_concept_id,
//   ix_job_ads_region_concept_id, ix_job_ads_employment_type_concept_id) hjälper
//   i filter-predikaten (ApplyFilter WHERE) men inte i rank-CASE:n (ORDER BY).
//
//   DEFAULT-SORT-BASLINJEN (PublishedAtDesc) kör samma Seq Scan + Sort men med
//   en enklare sorteringsnyckel (published_at DESC, id). Plan-shape är identisk —
//   ingen av dem gynnas av ett partiellt B-tree-index på published_at eftersom
//   Postgres' seqscan+sort är billigare för stora fraktioner av tabellen (>~5 %).
//   Dotnet-architect bekräftade: PublishedAtDesc-sorten "ships within budget" =
//   match-sortens CEILING för vad som är budgetmässigt acceptabelt (samma korpus,
//   samma Seq Scan, rankberäkning är marginalöverhead).
//
//   DELTAS:
//     - Rank-CASE-beräkning per rad (CASE WHEN ... THEN ... END) är ren CPU utan
//       I/O — marginalkostnad mätt i microsekunder per rad, inte millisekunder.
//       Heuristisk uppskattning: 54 000 rader × ~1-2 µs/CASE ≈ 50-100 ms.
//     - CountAsync kör SET LOCAL enable_seqscan=off + SELECT COUNT(*) i en
//       separat transaktion: TD-94:s bitmap-plan-tvång — men utan q-parameter
//       kör en COUNT utan FTS-predikat som inte lider av TOAST-detoast-problemet.
//       Planner väljer sannolikt Seq Scan (billigare utan q-predikat) eller
//       statuskolumns partiella index. Kostnad: låg (< 20 ms varm, utan q).
//
//   INDEXREKOMMENDATION (se detaljerat i review-rapporten
//   docs/reviews/2026-06-19-f4-14-sort-by-match-perf.md):
//     Partiellt B-tree-index (published_at DESC) WHERE status='Active' AND
//     deleted_at IS NULL kan INTE serva match-sort-rank — det vore bara relevant
//     för en ren PublishedAtDesc-sort. Dotnet-architect har redan bekräftat att
//     PublishedAtDesc-sorten håller budget utan det. Match-sort har samma eller
//     marginellt högre plankostnad. Indexet är INTE nödvändigt i detta PR;
//     det är ett forward-note att addera OM mätt regression kräver det.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class MatchSortScenarios
{
    /// <summary>
    /// Klass (a) read-query/list — Klas-låst p95 ≤ 300 ms (ADR 0045 Beslut 1).
    /// Återanvänder samma budget-konstant-mönster som <see cref="LandingStatsScenarios"/>;
    /// match-sort är GET /api/v1/job-ads?sortBy=5 = klass (a) (parity q-count,
    /// facet-counts).
    /// </summary>
    public const int Class_A_P95_BudgetMs = LandingStatsScenarios.Class_A_P95_BudgetMs;

    /// <summary>
    /// Klass (a) read-query/list — p99-observation-mål 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = LandingStatsScenarios.Class_A_P99_ObserveMs;

    // Route-kontraktet LÅST i F4-14 (ADR 0076 Decision 4/5):
    //   GET /api/v1/job-ads?sortBy=5
    //   sortBy=5 = ListJobAdsSort.MatchDesc (enum-binding case-insensitiv per namn
    //   eller numerisk; numerisk vald för att undvika ASP.NET Core-beroende på
    //   enum-name vs value i query-string-parsing).
    private const string JobAdsPath = "/api/v1/job-ads";

    // ListJobAdsSort.MatchDesc = 5 (F4-14, ADR 0076).
    private const int SortByMatchDesc = 5;

    // ListJobAdsSort.PublishedAtDesc = 0 (default/fallback).
    private const int SortByPublishedAtDesc = 0;

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön. Saknas den körs scenariot utan
    /// Authorization → 401 → fail-count → BudgetReporter-warning. Token-källans
    /// wiring görs i körnings-miljön (CI eller lokal dev-test-konto), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    /// <summary>
    /// Läser ett valfritt occupation-group concept-id ur miljön för Typical-scenariot.
    /// Sätts till ett reellt id från loadtest-DB:ns aktiva annons-set, t.ex.
    /// "iZge_5CT_Ahu" (Systemutvecklare). Utelämnas → Typical kör utan filter
    /// (WorstCase-semantik men under Typical-scenariot — synlig i rapporten).
    /// </summary>
    private static string? OccupationGroupId =>
        Environment.GetEnvironmentVariable("LOADTEST_OCCUPATION_GROUP_ID");

    // Bygger en GET /api/v1/job-ads-request med angivna query-parametrar och
    // Authorization-header om LOADTEST_BEARER_TOKEN finns.
    private static Task<Response<HttpResponseMessage>> SendJobAdsRequestAsync(
        HttpClient httpClient, string baseUrl, string query)
    {
        var request = Http.CreateRequest("GET", $"{baseUrl}{JobAdsPath}?{query}")
            .WithHeader("Accept", "application/json");

        var token = BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
            request = request.WithHeader("Authorization", $"Bearer {token}");

        // 401 (saknad/ogiltig token) eller 429 (rate-limit-kollision) räknas
        // som FAIL i NBomber-mätningen. 401 = token saknas/utgången (kalibreringsfel).
        // 429 = rate-limit-kollision (lastformen behöver re-kalibreras).
        // BudgetReporter flaggar non-zero fail-count som ::warning:: separat.
        return Http.Send(httpClient, request);
    }

    /// <summary>
    /// WORST-CASE signal — sortBy=MatchDesc + INGA filter + angiven yrkesgrupp.
    ///
    /// Mäter den maximala handler-latensen: ListJobAdsQueryHandler anropar
    /// IMatchProfileBuilder.BuildFromPreferencesAsync (1 SELECT) + CountAsync
    /// (1 COUNT med bitmap-plan-tvång) + MatchSortedJobAdSearchQuery.SearchByMatchAsync
    /// (Seq Scan + grad-rank CASE per rad + Sort) på hela aktiva korpusen (~54k rader,
    /// no filter). Det är det skärpta fallet ADR 0045 klass (a) 300 ms-budgeten
    /// måste täcka.
    ///
    /// Kräver LOADTEST_BEARER_TOKEN + att användaren HAR angiven yrkespreferens.
    /// Utan angiven yrkespreferens → SSYK-gate aktiv → fallback-sort (FallbackNoProfile:s
    /// bana — mäter INTE match-sort-hot-path). BudgetReporter flaggar fail-count om 401.
    /// </summary>
    public static ScenarioProps WorstCase(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("match_sort_worst_case", async _ =>
            {
                // sortBy=5 (MatchDesc), pageSize=20 (FE:s default), ingen filter.
                // Detta är sämsta-fallet för match-sorten: Postgres rankar och
                // sorterar hela aktiva mängden (~54k rader) med grad-rank CASE.
                var response = await SendJobAdsRequestAsync(
                    httpClient, baseUrl,
                    $"sortBy={SortByMatchDesc}&pageSize=20");

                return response;
            })
            // WarmUp: värmer DB-buffercache (PostgreSQL shared_buffers) + EF-query-
            // plan-cache. p95-mätningen ska representera "normal warm-state prod-
            // trafik", inte cold-start. Längre warmup (15s) än landing-stats (5s)
            // eftersom Seq Scan + Sort på full korpus kräver mer shared_buffers-
            // uppvärmning än en enkel Redis-lookup.
            .WithWarmUpDuration(TimeSpan.FromSeconds(15))
            .WithLoadSimulations(
                // 0,25 RPS = 15 req/min = 25 % av ListReadPolicy 60/min-taket.
                // Lägre än andra klass (a)-scenarier (se kommentar ovan — tyngre
                // query + delade user-bucket med övriga scenarier i "all"-selector).
                // 0,25 RPS × 120s = 30 samples → statistisk nedre gräns Fas 1
                // (Tukey n=30 med brett konfidensintervall).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// TYPICAL signal — sortBy=MatchDesc + EN occupationGroup-filter.
    ///
    /// Mäter den typiska prod-förfrågan: en autentiserad användare med angiven
    /// yrkespreferens filtrerar på EN yrkesgrupp → filtermängden är en bråkdel av
    /// hela korpusen → ApplyFilter WHERE reducerar antalet rader Seq Scan+Sort
    /// hanterar. Rank-CASE beräknas på träffmängden (ej hela ~54k). Representerar
    /// den "normal user med preferens"-profilerad last.
    ///
    /// LOADTEST_OCCUPATION_GROUP_ID (valfri) sätts av CI/lokal kalibrering till ett
    /// reellt concept-id som finns i loadtest-DB:n. Utelämnas → ingen filter =
    /// WorstCase-semantik under Typical-scenariots namn (synligt i trend-JSON).
    ///
    /// Kräver LOADTEST_BEARER_TOKEN + användare med angiven yrkespreferens.
    /// </summary>
    public static ScenarioProps TypicalWithOccupationFilter(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("match_sort_typical_with_filter", async _ =>
            {
                // Bygg query-sträng: sortBy=MatchDesc + eventuellt occupationGroup-filter.
                var occupationId = OccupationGroupId;
                var query = string.IsNullOrWhiteSpace(occupationId)
                    ? $"sortBy={SortByMatchDesc}&pageSize=20"
                    : $"sortBy={SortByMatchDesc}&occupationGroup={Uri.EscapeDataString(occupationId)}&pageSize=20";

                var response = await SendJobAdsRequestAsync(httpClient, baseUrl, query);
                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 0,25 RPS = 15 req/min = 25 % av ListReadPolicy 60/min-taket.
                // Symmetri med WorstCase för att hålla summerad rate under taket
                // vid parallell körning (0,75 RPS totalt för alla match-sort-scenarier
                // = 45 req/min, under 60/min).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// FALLBACK/BASELINE signal — sortBy=MatchDesc utan angiven yrkespreferens.
    ///
    /// Mäter ADR 0076 Decision 7:s honest fallback: handler anropar
    /// IMatchProfileBuilder (1 SELECT), konstaterar tom SSYK-gate och delegerar
    /// till IJobAdSearchQuery.SearchAsync (default PublishedAtDesc-sort). Det är
    /// SAMMA kod-väg som den ordinarie ListJobAds-listan utan sortBy-parameter —
    /// skillnaden är en extra SELECT för profil-bygget som kort-circuits:as.
    ///
    /// Värdet är att mäta:
    ///   (a) Att fallback-latensen (inklusive profil-SELECT) håller budget.
    ///   (b) Att default-sorten (PublishedAtDesc) — den BEFINTLIGA baseline —
    ///       fortsätter hålla budget efter F4-14-lanseringen (regression guard).
    ///
    /// I PRAKTIKEN TESTAR DETTA: använd antingen (1) en testanvändare UTAN angiven
    /// yrkespreferens, eller (2) en testanvändare MED preferenser men utelämna
    /// LOADTEST_OCCUPATION_GROUP_ID OG ändra scenariots query — se kommentaren
    /// nedan. Scenariot är designat för fall (1): en riktig "fallback-user".
    ///
    /// Utan LOADTEST_BEARER_TOKEN → 401 → fail-count (samma signal som övriga).
    /// </summary>
    public static ScenarioProps FallbackNoProfile(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("match_sort_fallback_no_profile", async _ =>
            {
                // sortBy=5 (MatchDesc) → handler försöker match-sort → profil-lookup
                // → SSYK tom (ingen yrkespreferens) → fallback PublishedAtDesc-sort.
                // Mäter: 1 SELECT profil (kortsluts) + default-sort-kostnaden.
                // Inte ett eget SSYK-frånvaro-assert — det garanteras av testanvändarens
                // tillstånd i DB:n, inte av scenariot (scenariot mäter, garanterar ej).
                var response = await SendJobAdsRequestAsync(
                    httpClient, baseUrl,
                    $"sortBy={SortByMatchDesc}&pageSize=20");

                return response;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // 0,25 RPS = 15 req/min = 25 % av ListReadPolicy 60/min-taket.
                // Lägst prioritet av de tre scenarierna i "all"-selector; headroom
                // bevaras om FallbackNoProfile kör parallellt med WorstCase + Typical.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERING i Program.cs via selektor "match-sort" / "all".
// Alla tre scenarierna registreras under "match-sort":
//   WorstCase                 → "match_sort_worst_case"
//   TypicalWithOccupationFilter → "match_sort_typical_with_filter"
//   FallbackNoProfile         → "match_sort_fallback_no_profile"
//
// Körs med:
//   LOADTEST_SCENARIOS=match-sort \
//   LOADTEST_BEARER_TOKEN=<jwt-med-preferens> \
//   LOADTEST_OCCUPATION_GROUP_ID=iZge_5CT_Ahu \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
//   # Fallback-only (testanvändare UTAN yrkespreferens):
//   LOADTEST_SCENARIOS=match-sort \
//   LOADTEST_BEARER_TOKEN=<jwt-utan-preferens> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
// BudgetReporter emitterar ::warning:: vid p95 > 300 ms, exit 0 ovillkorligt
// (observe-only Fas 1). Flip till BLOCKING gate = Klas-GO-ratchet
// (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
// ─────────────────────────────────────────────────────────────────────────────
