// Jobbliggaren perf fitness function — GET /api/v1/job-ads?q=<term> q-COUNT hot path.
//
// KONTEXT (TD-94):
//   Den fria textsökningen utfärdar en COUNT-query för paginering. Före TD-94
//   körde COUNT-queryn sequential scan på ~44k rader vid höga och moderata FTS-
//   termer, med mätt latens 9,3 s (kall "fritext") — ett flagrant ADR 0045-brott.
//   TD-94 fixade detta via C1 SET LOCAL enable_seqscan=off + C3 title-LIKE≥3-gate
//   i Jobbliggaren.Infrastructure/JobAds/JobAdSearchQuery.cs.
//   Representativa varma mätningar efter fix: ai 15 ms, utvecklare 96 ms,
//   lärare 116 ms. Denna fitness function vaktar mot regression (t.ex. om
//   enable_seqscan-GUC:en plockas bort utan medvetenhet).
//
// ─────────────────────────────────────────────────────────────────────────────
// BUDGET (verbatim ur ADR 0045 Beslut 1 — aldrig uppfunnen):
//   q-COUNT är del av ListJobAdsQuery → klass (a) read-query/list
//     p95 ≤ 300 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//     p99 600 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT-PROFIL:
//   - Auth-gated (RequireAuthorization via grupp, ADR 0005). LOADTEST_BEARER_TOKEN
//     sätts av CI-jobbet/lokalt loadtest-körning — exakt samma mekanism som
//     FacetCountsScenarios. Saknas token → 401 → fail-count → BudgetReporter-warning.
//   - ListReadPolicy: 60 req/min per UserId (claim "sub"), fixed window.
//   - Handler kör ListJobAdsQuery → JobAdSearchQuery.cs → SQL med COUNT +
//     SELECT. COUNT-pathen är det TD-94 fixade; scenariot mäter båda
//     (handler-latens inkluderar both SELECT och COUNT).
//   - Cache-Control: ingen explicit (privat, auth-gated) — ingen proxy-cache
//     absorberar, varje request når DB.
//
// TERM-PROFIL (representativa planer, CTO-disciplin: kalibrera mot fakta):
//   "ai"         → kort selektiv FTS-term (2 tecken, plan: GIN-index + titel-LIKE)
//   "utvecklare" → hög-frekvent ≥3-tecken-term (plan: titel-LIKE-gate aktiv)
//   "lärare"     → moderat ≥3-tecken-term med diakritik (täcker UTF-8-kodväg)
//   Termer roteras round-robin i scenariot — jämnt fördelade samples per plan.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, ej gissning):
//   ListReadPolicy 60/min per UserId. En total-rate på 1 RPS (cycling över de
//   3 termerna) = 60 req/min = exakt i takets överkant. VALT: 0,5 RPS total
//   (1 request var 2:a sekund) = 30 req/min = 50 % av taket → headroom mot
//   rate-limit-kollision när scenariot körs parallellt med andra scenarier
//   inom samma "all"-selector (delar user-bucket med samma token).
//   0,5 RPS × 120s = 60 samples; statistiskt tillräckligt för observe-only
//   trend-signal (Tukey: n≥30 räcker för p95 ± brett konfidensintervall Fas 1;
//   n=60 ger bättre granularitet). Höj till 1 RPS om bara detta scenario körs
//   isolerat med LOADTEST_SCENARIOS=q-count.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class FreeTextCountScenarios
{
    /// <summary>
    /// Klass (a) read-query/list — Klas-låst p95 ≤ 300 ms (ADR 0045 Beslut 1).
    /// Återanvänder samma budget-konstant-mönster som <see cref="LandingStatsScenarios"/>;
    /// free-text list-query är klass (a) (TD-94 hot path vaktas av denna fitness function).
    /// </summary>
    public const int Class_A_P95_BudgetMs = LandingStatsScenarios.Class_A_P95_BudgetMs;

    /// <summary>
    /// Klass (a) read-query/list — p99-observation-mål 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = LandingStatsScenarios.Class_A_P99_ObserveMs;

    // Termer som täcker de tre relevanta exekveringsplanerna (TD-94 C1/C3).
    // Rotera i fast ordning → jämna samples per plan.
    private static readonly string[] SearchTerms = ["ai", "utvecklare", "lärare"];

    // Rounda round-robin index (ej thread-safe — NBomber kör scenariot
    // single-threaded per Simulation.Inject-worker; index är scenario-scoped).
    private static int _termIndex;

    private const string JobAdsPath = "/api/v1/job-ads";

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön. Saknas den körs scenariot utan
    /// Authorization → 401 → fail-count → BudgetReporter-warning. Token-källans
    /// wiring görs i körnings-miljön (CI eller lokal dev-test-konton,
    /// se docs/runbooks/frontend-visual-verification.md cred-path), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    /// <summary>
    /// PRIMÄR signal — q-COUNT hot path med round-robin termrotation.
    ///
    /// Vaktar mot regression av TD-94-fixet (SET LOCAL enable_seqscan=off +
    /// title-LIKE≥3-gate i JobAdSearchQuery.cs). En framtida borttagning av
    /// enable_seqscan-GUC:en eller felaktig query-plan-regression fångas som
    /// p95-överskridande mot ADR 0045 klass (a) 300 ms-budgeten.
    ///
    /// Mäter handler-latens för ListJobAdsQuery (SELECT + COUNT), ej enbart
    /// COUNT-isolerat — det är den latens användaren upplever (ADR 0045 Beslut 1
    /// mätpunkt = server-side handler-latens).
    /// </summary>
    public static ScenarioProps QCountHotPath(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("free_text_q_count", async _ =>
            {
                // Round-robin term: ai → utvecklare → lärare → ai → ...
                // Interlocked säkrar mot potentiell framtida NBomber-parallellism,
                // men primärt kalibrerat för Inject-baserad single-worker.
                var idx = Interlocked.Increment(ref _termIndex);
                var term = SearchTerms[idx % SearchTerms.Length];

                // pageSize=1 ger minsta möjliga SELECT-kostnad och isolerar
                // COUNT-pathen som signalen. Totalräknaren (totalCount i svaret)
                // är det TD-94 fixade — vi mäter om den håller budget.
                var url = $"{baseUrl}{JobAdsPath}?q={Uri.EscapeDataString(term)}&pageSize=1";
                var request = Http.CreateRequest("GET", url)
                    .WithHeader("Accept", "application/json");

                var token = BearerToken;
                if (!string.IsNullOrWhiteSpace(token))
                    request = request.WithHeader("Authorization", $"Bearer {token}");

                // 401 (saknad/ogiltig token) eller 429 (rate-limit-kollision)
                // räknas som FAIL i NBomber-mätningen och visas av BudgetReporter
                // som en separat ::warning:: — kalibreringsfel, inte hot-path-signal.
                return await Http.Send(httpClient, request);
            })
            // WarmUp sätter DB i warm state (PostgreSQL buffer cache + query-plan
            // cache uppvärmd). p95-mätningen ska representera "normal prod-trafik
            // mot varm DB", inte cold-start — TD-94-fixets egna mätningar är
            // gjorda warm (ai 15 ms, utvecklare 96 ms, lärare 116 ms).
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 0,5 RPS = 30 req/min = 50 % av ListReadPolicy 60/min-taket.
                // Headroom mot rate-limit-kollision vid parallell scenariokörning
                // (delar user-bucket med FacetCounts-scenarierna om "all"-selector).
                // 0,5 RPS × 120s = 60 samples → statistiskt tillräcklig p95-signal
                // (Tukey n≥30 för observe-only trend Fas 1).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERAS via selektor "q-count" eller "all" i Program.cs.
// LOADTEST_BEARER_TOKEN wireas per körning (lokalt: dev-test-kontot,
// se docs/runbooks/frontend-visual-verification.md cred-path).
//
// BudgetReporter emitterar ::warning:: vid p95-överskridande, exit 0
// ovillkorligt (observe-only Fas 1). Flip till BLOCKING gate = medveten
// Klas-GO-ratchet (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
//
// Körs med:
//   LOADTEST_SCENARIOS=q-count LOADTEST_BEARER_TOKEN=<jwt> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
// ─────────────────────────────────────────────────────────────────────────────
