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
//   (a) read-query/list  : p95 300 ms   (Klas-låst — landing-stats kläm denna)
//   (b) typeahead/suggest: p95 150 ms   (Klas-låst — ej mätt denna PR)
//   (c) command/write    : p95 400 ms   (CTO-satt — ej mätt denna PR)
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
