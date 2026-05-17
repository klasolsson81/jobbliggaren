// JobbPilot load-test-baslinje — ADR 0045 (performance-budgetar och fitness functions).
//
// SCOPE: Detta är BASLINJE-scaffoldet (CI-infra steg 3). De faktiska hot-path-
// scenarierna mot budgetarna nedan skrivs av perf-test-writer-agenten (steg 4,
// SIST i sekvensen — CTO roster-gap 2026-05-17). Detta projekt finns så att
// agenten har en körbar runner att bygga vidare på, och så att CI:s observe-
// only `loadtest`-jobb har något att exekvera redan vid leverans.
//
// OBSERVE-ONLY FAS 1 (ADR 0045 Beslut 5): processen returnerar alltid 0.
// Budget-överskridande loggas som `::warning::` (GitHub-annotation) — den
// blockerar INTE CI (ej i ci.needs). Flip till blockerande = medveten ratchet
// vid Klas-GO (ADR 0045 Beslut 6), aldrig tyst default.
//
// ADR 0045 Beslut 1 — server-side p95-budgetar (det agenten ska mäta mot):
//   (a) read-query/list  : p95 300 ms   (Klas-låst — produkt/UX/kostnad)
//   (b) typeahead/suggest: p95 150 ms   (Klas-låst — produkt/UX/kostnad)
//   (c) command/write    : p95 400 ms   (CTO-satt)
//   (d) ingestion        : ≥ 200 jobb/min sustained (CTO-satt)

using NBomber.CSharp;
using NBomber.Http.CSharp;

// Mot vilken instans testet körs. CI-jobbet sätter denna mot en lokalt startad
// API-container; lokalt default = dev-API. Aldrig mot prod (ADR 0045 / §9.2).
var baseUrl = Environment.GetEnvironmentVariable("LOADTEST_BASE_URL")
              ?? "http://localhost:8080";

using var httpClient = new HttpClient();

// Baslinje-scenario: liveness-probe (`/api/health`). Medvetet DB-fritt — det
// mäter ren request-pipeline-overhead som kalibrerings-referens (CTO: kalibrera
// mot uppmätt baslinje, ej gissning). Hot-path-scenarierna (sök/typeahead/
// command) läggs till av perf-test-writer-agenten mot Beslut 1-budgetarna.
var baseline = Scenario.Create("api_health_baseline", async context =>
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

NBomberRunner
    .RegisterScenarios(baseline)
    .WithReportFolder("loadtest-reports")
    .WithReportFormats(ReportFormat.Md, ReportFormat.Csv, ReportFormat.Html)
    .Run();

// Observe-only: oavsett NBomber-resultat returnerar processen 0 Fas 1.
// Budget-domen (p95 vs Beslut 1) görs av agentens scenarier + CI-jobbets
// ::warning::-emittering, inte av en hård exit-kod denna fas.
return 0;
