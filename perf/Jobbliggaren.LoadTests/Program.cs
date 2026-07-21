// Jobbliggaren load-test-runner — ADR 0045 (performance-budgetar och fitness functions).
//
// SCOPE: NBomber-baserad fitness-function-runner. Detta projekt är medvetet
// utanför Jobbliggaren.sln (build.yml backend-jobb + coverage-gaten plockar EJ
// upp det). Konsole-app, kör enbart av det dedikerade observe-only `loadtest`-
// jobbet i build.yml + lokalt vid kalibrering.
//
// OBSERVE-ONLY FAS 1 (ADR 0045 Beslut 5): processen returnerar alltid 0.
// Budget-överskridande loggas som ::warning::-annotation via BudgetReporter.
// Flip till blockerande = medveten ratchet vid Klas-GO (ADR 0045 Beslut 6).
//
// ADR 0045 Beslut 1 — server-side p95-budgetar mätta:
//   (a) read-query/list  : p95 300 ms   (Klas-låst — landing-stats + fritext + facet + match-sort)
//   (b) typeahead/suggest: p95 150 ms   (Klas-låst — SuggestScenarios, #753)
//   (c) command/write    : p95 400 ms   (CTO-satt — MeSeenWriteScenarios, #753)
//   (d) ingestion        : ≥ 200 jobb/min sustained (ej mätt denna PR)
//
// KÖRLÄGEN:
//   dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//     → kör baseline + alla aktiverade hot-path-scenarier mot LOADTEST_BASE_URL.
//
//   LOADTEST_SCENARIOS=baseline-only dotnet run ...
//     → kör enbart baseline-health (default i CI loadtest-jobbet idag, eftersom
//       ephemeral API mot landing-stats kräver Redis/Worker-stack — wiras upp
//       när CI får docker-compose-stöd).
//
//   LOADTEST_SCENARIOS=landing-stats dotnet run ...
//     → kör baseline + landing-stats-scenarierna.
//
//   LOADTEST_SCENARIOS=q-count dotnet run ...
//     → kör baseline + q-COUNT-hot-path-scenariot (TD-94 regression guard).
//
//   LOADTEST_SCENARIOS=suggest dotnet run ...
//     → kör baseline + typeahead/suggest-scenariot (#753, klass (b) 150 ms).
//
//   LOADTEST_SCENARIOS=me-seen-write dotnet run ...
//     → kör baseline + /me/jobs/seen write-scenariot (#753, klass (c) 400 ms).
//
//   LOADTEST_SCENARIOS=all dotnet run ...
//     → kör allt.

using Jobbliggaren.LoadTests.Reporting;
using Jobbliggaren.LoadTests.Scenarios;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Http.CSharp;

// Mot vilken instans testet körs. CI-jobbet sätter denna mot en lokalt startad
// API-container; lokalt default = dev-API. Aldrig mot prod (ADR 0045 / §9.2).
var baseUrl = Environment.GetEnvironmentVariable("LOADTEST_BASE_URL")
              ?? "http://localhost:8080";

// Scenario-selektor. Default = baseline-only så CI:s nuvarande loadtest-jobb
// (som idag inte startar en API-stack) inte blir bullriga med connection-
// refused. När docker-compose-baserat ephemeral API är wirat in i jobbet
// flippas defaulten till "landing-stats".
var scenarioSelector = (Environment.GetEnvironmentVariable("LOADTEST_SCENARIOS")
                        ?? "baseline-only")
    .Trim()
    .ToLowerInvariant();

using var httpClient = new HttpClient
{
    // Längre timeout än default (100s) skulle bara dölja patologiska budget-
    // brott. 5s är generöst mot klass (a) 300 ms-budget × 16 (felmarginal mot
    // p99 600 ms + nät-jitter); allt över räknas som ::fail:: och blir
    // BudgetReporter-warning, inte hängd request.
    Timeout = TimeSpan.FromSeconds(5),
};

// Baslinje-scenario: liveness-probe (/api/health). Medvetet DB-fritt — mäter
// ren request-pipeline-overhead som kalibrerings-referens (CTO: kalibrera mot
// uppmätt baslinje, ej gissning). Har ingen ADR-budget — körs ALLTID för att
// ge oss en "house-keeping"-trend mot vilken hot-path-mätningar kan jämföras.
var baseline = Scenario.Create("api_health_baseline", async _ =>
    {
        var request = Http.CreateRequest("GET", $"{baseUrl}/api/health");
        var response = await Http.Send(httpClient, request);
        return response;
    })
    .WithoutWarmUp()
    .WithLoadSimulations(
        Simulation.Inject(
            rate: 10,
            interval: TimeSpan.FromSeconds(1),
            during: TimeSpan.FromSeconds(15)));

// Scenarios som registreras med NBomberRunner.
var scenarios = new List<ScenarioProps> { baseline };

// Scenario-budgetar (ADR 0045 Beslut 1). Scenario utan budget loggas av
// BudgetReporter men jämförs ej — baseline tillhör den kategorin.
var scenarioBudgets = new Dictionary<string, int>();

if (scenarioSelector is "landing-stats" or "all")
{
    var landingWarm = LandingStatsScenarios.CacheHitWarmPath(httpClient, baseUrl);
    var landingCold = LandingStatsScenarios.CacheMissColdPath(httpClient, baseUrl);

    scenarios.Add(landingWarm);
    scenarios.Add(landingCold);

    scenarioBudgets[landingWarm.ScenarioName] = LandingStatsScenarios.Class_A_P95_BudgetMs;
    scenarioBudgets[landingCold.ScenarioName] = LandingStatsScenarios.Class_A_P95_BudgetMs;
}

// Fas E2c (ADR 0067 Beslut 4) — facet-counts-endpointen är rest; D1-parkerade
// scenariot aktiveras. Kräver LOADTEST_BEARER_TOKEN (auth-gated) — utan den
// blir requests 401 → fail-count → BudgetReporter-warning (avsiktligt synligt).
if (scenarioSelector is "facet-counts" or "all")
{
    var facetHeavy = FacetCountsScenarios.OccupationGroupHeavy(httpClient, baseUrl);
    var facetReflected = FacetCountsScenarios.ReflectedWithActiveFilter(httpClient, baseUrl);

    scenarios.Add(facetHeavy);
    scenarios.Add(facetReflected);

    scenarioBudgets[facetHeavy.ScenarioName] = FacetCountsScenarios.Class_A_P95_BudgetMs;
    scenarioBudgets[facetReflected.ScenarioName] = FacetCountsScenarios.Class_A_P95_BudgetMs;
}

// TD-94 (perf-ratchet) — q-COUNT hot path. Vaktar mot regression av
// enable_seqscan=off-GUC:en + title-LIKE≥3-gate i JobAdSearchQuery.cs.
// Kräver LOADTEST_BEARER_TOKEN (auth-gated ListReadPolicy) — utan den
// blir requests 401 → fail-count → BudgetReporter-warning (avsiktligt synligt).
if (scenarioSelector is "q-count" or "all")
{
    var qCount = FreeTextCountScenarios.QCountHotPath(httpClient, baseUrl);

    scenarios.Add(qCount);

    scenarioBudgets[qCount.ScenarioName] = FreeTextCountScenarios.Class_A_P95_BudgetMs;
}

// F4-14 (ADR 0076 Decision 4/5) — GET /api/v1/job-ads?sortBy=5 (MatchDesc).
// Klass (a) p95 ≤ 300 ms (ADR 0045 Beslut 1). Auth-gated (ListReadPolicy
// 60/min per UserId — kräver LOADTEST_BEARER_TOKEN). Tre dimensioner:
//   WorstCase:            MatchDesc + inga filter (rank-CASE på ~54k rader).
//   TypicalWithFilter:    MatchDesc + EN occupationGroup-filter (reducerad mängd).
//   FallbackNoProfile:    MatchDesc utan angiven yrkespreferens → PublishedAtDesc-fallback.
// Kräver LOADTEST_BEARER_TOKEN. WorstCase + TypicalWithFilter kräver dessutom
// att testanvändaren har angiven yrkespreferens (SsykGroupConceptIds icke-tom).
if (scenarioSelector is "match-sort" or "all")
{
    var matchSortWorst = MatchSortScenarios.WorstCase(httpClient, baseUrl);
    var matchSortTypical = MatchSortScenarios.TypicalWithOccupationFilter(httpClient, baseUrl);
    var matchSortFallback = MatchSortScenarios.FallbackNoProfile(httpClient, baseUrl);

    scenarios.Add(matchSortWorst);
    scenarios.Add(matchSortTypical);
    scenarios.Add(matchSortFallback);

    scenarioBudgets[matchSortWorst.ScenarioName] = MatchSortScenarios.Class_A_P95_BudgetMs;
    scenarioBudgets[matchSortTypical.ScenarioName] = MatchSortScenarios.Class_A_P95_BudgetMs;
    scenarioBudgets[matchSortFallback.ScenarioName] = MatchSortScenarios.Class_A_P95_BudgetMs;
}

// F4-13 (ADR 0076 Decision 5) — POST /api/v1/me/job-ad-match-tags.
// Klass (a) p95 ≤ 300 ms (ADR 0045 Beslut 1). Anonym-tolerant men kräver
// LOADTEST_BEARER_TOKEN för autentiserad hot-path-mätning (2 DB-round-trips +
// in-memory scoring). Utan token kör scenariot anonymt → handler short-circuit:ar
// → latensen mäter pipeline-overhead men INTE 2 DB-round-trips (tyst svag signal).
// Standard: 20-ID-payload (FE:s pageSize). Stress: 100-ID-payload (validator-cap).
if (scenarioSelector is "match-tags" or "all")
{
    var matchTagsNormal = MatchTagBatchScenarios.NormalPageSizeBatch(httpClient, baseUrl);

    scenarios.Add(matchTagsNormal);

    scenarioBudgets[matchTagsNormal.ScenarioName] = MatchTagBatchScenarios.Class_A_P95_BudgetMs;
}

if (scenarioSelector is "match-tags-stress" or "all")
{
    var matchTagsStress = MatchTagBatchScenarios.StressCapBatch(httpClient, baseUrl);

    scenarios.Add(matchTagsStress);

    scenarioBudgets[matchTagsStress.ScenarioName] = MatchTagBatchScenarios.Class_A_P95_BudgetMs;
}

// #312 (ADR 0115 R1) — GET /api/v1/saved-searches/new-results-count per-search COUNT fan-out.
// Class (a) p95 <= 300 ms (ADR 0045 Decision 1). Auth-gated (LOADTEST_BEARER_TOKEN,
// ListReadPolicy). R1: this fitness function MUST clear ADR 0045 before the FE surface
// (/sokningar re-activation, ADR 0115 Decision "(2)") goes live — see
// SavedSearchNewResultsCountScenarios.cs header for the fixture preconditions (the target
// account must be provisioned at the fan-out ceiling for a meaningful PRIMARY signal; without
// it this measures a conservative floor, not the worst case).
if (scenarioSelector is "saved-search-notify" or "all")
{
    var savedSearchNotifyCeiling =
        SavedSearchNewResultsCountScenarios.FanOutCeiling(httpClient, baseUrl);

    scenarios.Add(savedSearchNotifyCeiling);

    scenarioBudgets[savedSearchNotifyCeiling.ScenarioName] =
        SavedSearchNewResultsCountScenarios.Class_A_P95_BudgetMs;
}

if (scenarioSelector is "saved-search-notify-typical" or "all")
{
    var savedSearchNotifyTypical =
        SavedSearchNewResultsCountScenarios.TypicalNotificationLoad(httpClient, baseUrl);

    scenarios.Add(savedSearchNotifyTypical);

    scenarioBudgets[savedSearchNotifyTypical.ScenarioName] =
        SavedSearchNewResultsCountScenarios.Class_A_P95_BudgetMs;
}

// #753 (epik #737) — GET /api/v1/job-ads/suggest typeahead. Klass (b) p95 ≤ 150 ms
// (ADR 0045 Beslut 1, Klas-låst — den striktaste budgeten). Auth-gated (SuggestPolicy
// 30/10s per UserId — kräver LOADTEST_BEARER_TOKEN; saknas → 401 → fail-count →
// BudgetReporter-warning). Egen policy-partition → konkurrerar bara med sig självt om
// Suggest-bucketen även i "all".
if (scenarioSelector is "suggest" or "all")
{
    var suggest = SuggestScenarios.SuggestTypeahead(httpClient, baseUrl);

    scenarios.Add(suggest);

    scenarioBudgets[suggest.ScenarioName] = SuggestScenarios.Class_B_P95_BudgetMs;
}

// #753 (epik #737) — POST /api/v1/me/jobs/seen watermark-write. Klass (c) p95 ≤ 400 ms
// (ADR 0045 Beslut 1, CTO-satt). Auth-gated write (MeWritePolicy 30/60s — kräver
// LOADTEST_BEARER_TOKEN). Tom body → clock-now-fallback → genuin monoton UPDATE per
// sample (ej no-op; se MeSeenWriteScenarios NO-OP-FÄLLAN). MUTERAR dev-DB:ns test-
// användar-watermark → endast ensam stack-owner får köra den mot delade DB:n (§6.5).
if (scenarioSelector is "me-seen-write" or "all")
{
    var seenWrite = MeSeenWriteScenarios.SeenWatermarkWrite(httpClient, baseUrl);

    scenarios.Add(seenWrite);

    scenarioBudgets[seenWrite.ScenarioName] = MeSeenWriteScenarios.Class_C_P95_BudgetMs;
}

Console.WriteLine(
    $"::notice::Load-test runner startar — baseUrl={baseUrl}, " +
    $"scenarios=[{string.Join(", ", scenarios.Select(s => s.ScenarioName))}], " +
    $"selector={scenarioSelector}");

var stats = NBomberRunner
    .RegisterScenarios([.. scenarios])
    .WithReportFolder("loadtest-reports")
    .WithReportFormats(ReportFormat.Md, ReportFormat.Csv, ReportFormat.Html)
    .Run();

// Budget-rapport mot ADR 0045 Beslut 1. Observe-only — emitterar ::warning::
// vid p95-överskridande, exit-koden förblir 0 (nedan).
var trendPath = Path.Combine(
    "artifacts",
    "perf",
    $"landing-stats-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");

BudgetReporter.Report(stats, scenarioBudgets, trendPath);

// Observe-only Fas 1: oavsett NBomber-resultat returnerar processen 0.
// Budget-domen är emitterad som annotation + JSON-trend ovan; den blockerar
// EJ CI denna fas. Flip = Klas-GO-ratchet (ADR 0045 Beslut 6).
return 0;
