// Jobbliggaren perf fitness function — GET /api/v1/job-ads/suggest typeahead hot path.
//
// KONTEXT (#753, epik #737 — audit 2026-07-10, finding m1-typeahead-budget-unmeasured):
//   ADR 0045 låser fyra budget-klasser men NBomber-harnessen hade före denna fil
//   ENBART klass (a)-scenarier. Klass (b) typeahead/suggest — den STRIKTASTE budgeten
//   (Klas-låst 150 ms) — hade noll instrument. Detta scenario stänger den luckan så en
//   framtida ratchet (ADR 0045 Beslut 6) har en signal att ratcheta mot.
//
// ─────────────────────────────────────────────────────────────────────────────
// BUDGET (verbatim ur ADR 0045 Beslut 1 — aldrig uppfunnen):
//   /suggest är typeahead → klass (b) typeahead/suggest
//     p95 ≤ 150 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//     p99 300 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT-PROFIL:
//   - GET /api/v1/job-ads/suggest?prefix=<term>&limit=10 (JobAdsEndpoints.cs).
//   - Auth-gated (hela /api/v1/job-ads-gruppen är RequireAuthorization, ADR 0005).
//     LOADTEST_BEARER_TOKEN sätts av CI-jobbet/lokal loadtest-körning — exakt samma
//     mekanism som FreeTextCountScenarios/FacetCountsScenarios. Saknas token → 401 →
//     fail-count → BudgetReporter-warning (avsiktligt synligt).
//   - Egen SuggestPolicy: TokenBucket 30/10s per UserId (claim "sub") — least common
//     mechanism (Saltzer/Schroeder, RateLimitingOptions.Suggest). Egen partition, delas
//     INTE med ListRead/FacetCounts → detta scenario konkurrerar bara med sig självt om
//     Suggest-bucketen även i "all"-selectorn.
//   - Handler (SuggestJobAdTermsQuery) unionar taxonomi-snapshot-labels + job_ads.Title
//     ILIKE-prefix (ADR 0042 Beslut C / 0067 5a). Validator-golv: prefix ≥2 tecken,
//     limit 1–20 (SuggestJobAdTermsQueryValidator).
//   - Cache-Control: private, auth-gated — ingen proxy-cache absorberar; varje request
//     når handlern.
//
// PREFIX-PROFIL (representativa svenska yrkes-prefix, CTO-disciplin: kalibrera mot fakta):
//   "ut"  → utvecklare/utredare (kort ≥2, hög-frekvent titel-prefix)
//   "sju" → sjuksköterska (≥3, diakritik-fri men vanlig vårdtitel)
//   "lär" → lärare (≥3 med diakritik — täcker UTF-8-kodvägen i ILIKE + taxonomi-union)
//   "eko" → ekonom/ekonomiassistent (≥3, taxonomi-label-träff)
//   "pro" → projektledare/programmerare (≥3, bred titel-prefix-fläkt)
//   Termer roteras round-robin → jämnt fördelade samples per prefix-selektivitet.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, ej gissning):
//   SuggestPolicy 30/10s per UserId (~3 req/s sustainable refill). En total-rate på
//   1 RPS = 10 req per 10s-fönster ≈ 33 % av bucket-kapaciteten (30) och av refill-
//   raten (~3/s) → generös headroom mot 429. 1 RPS × 120s = 120 samples → statistiskt
//   stabil p95-distribution (Tukey n≥100 för p95 ± rimligt konfidensintervall Fas 1).
//   Höj bara om detta scenario körs isolerat med LOADTEST_SCENARIOS=suggest.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class SuggestScenarios
{
    /// <summary>
    /// Klass (b) typeahead/suggest — Klas-låst p95 ≤ 150 ms (ADR 0045 Beslut 1, verbatim).
    /// Klass (b) får sin första kanoniska ägare här (samma mönster som
    /// <see cref="LandingStatsScenarios"/> äger klass (a)); framtida syskon-scenarier av
    /// klass (b) re-exporterar via <c>= SuggestScenarios.Class_B_P95_BudgetMs</c>.
    /// </summary>
    public const int Class_B_P95_BudgetMs = 150;

    /// <summary>
    /// Klass (b) typeahead/suggest — p99-observation-mål 300 ms (observe-only Fas 1).
    /// Dokumentär: BudgetReporter jämför enbart p95 mot budget (BudgetReporter.cs).
    /// </summary>
    public const int Class_B_P99_ObserveMs = 300;

    // Representativa yrkes-prefix (≥2 tecken, klarar SuggestJobAdTermsQueryValidator-golvet).
    // Rotera i fast ordning → jämna samples per prefix-selektivitet.
    private static readonly string[] Prefixes = ["ut", "sju", "lär", "eko", "pro"];

    // Round-robin-index (Interlocked mot potentiell framtida NBomber-parallellism;
    // primärt kalibrerat för Inject-baserad single-worker, scenario-scoped).
    private static int _prefixIndex;

    private const string SuggestPath = "/api/v1/job-ads/suggest";

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön. Saknas den körs scenariot utan
    /// Authorization → 401 → fail-count → BudgetReporter-warning. Token-källans
    /// wiring görs i körnings-miljön (CI eller lokal dev-test-konto,
    /// se docs/runbooks/frontend-visual-verification.md cred-path), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    /// <summary>
    /// PRIMÄR signal — typeahead-suggest hot path med round-robin prefix-rotation.
    ///
    /// Mäter handler-latens för SuggestJobAdTermsQuery (taxonomi-label-union +
    /// job_ads.Title ILIKE-prefix) mot ADR 0045 klass (b) 150 ms-budgeten — den
    /// striktaste, keystroke-heta ytan (job-ad-typeahead.tsx). En regression i
    /// ILIKE-prefix-planen eller taxonomi-snapshot-lookupen fångas som p95-överskridande.
    /// </summary>
    public static ScenarioProps SuggestTypeahead(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("job_ads_suggest_typeahead", async _ =>
            {
                // Round-robin prefix: ut → sju → lär → eko → pro → ut → ...
                var idx = Interlocked.Increment(ref _prefixIndex);
                var prefix = Prefixes[idx % Prefixes.Length];

                var url = $"{baseUrl}{SuggestPath}?prefix={Uri.EscapeDataString(prefix)}&limit=10";
                var request = Http.CreateRequest("GET", url)
                    .WithHeader("Accept", "application/json");

                var token = BearerToken;
                if (!string.IsNullOrWhiteSpace(token))
                    request = request.WithHeader("Authorization", $"Bearer {token}");

                // 401 (saknad/ogiltig token) eller 429 (rate-limit-kollision) räknas
                // som FAIL i NBomber-mätningen och visas av BudgetReporter som en separat
                // ::warning:: — kalibreringsfel, inte hot-path-signal.
                return await Http.Send(httpClient, request);
            })
            // WarmUp värmer DB-buffercache + EF-query-plan-cache + taxonomi-snapshot-
            // singleton. p95-mätningen ska representera "normal prod-trafik mot varm
            // instans", inte cold-start. Parity FreeTextCountScenarios (10s warmup).
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 1 RPS = 10 req/10s-fönster ≈ 33 % av SuggestPolicy 30/10s-bucketen →
                // headroom mot 429. 1 RPS × 120s = 120 samples → stabil p95-signal
                // (Tukey n≥100 för observe-only trend Fas 1).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERAS via selektor "suggest" eller "all" i Program.cs.
// LOADTEST_BEARER_TOKEN wireas per körning (lokalt: dev-test-kontot,
// se docs/runbooks/frontend-visual-verification.md cred-path).
//
// BudgetReporter emitterar ::warning:: vid p95 > 150 ms, exit 0 ovillkorligt
// (observe-only Fas 1). Flip till BLOCKING gate = medveten Klas-GO-ratchet
// (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
//
// Körs med:
//   LOADTEST_SCENARIOS=suggest LOADTEST_BEARER_TOKEN=<jwt> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
// ─────────────────────────────────────────────────────────────────────────────
