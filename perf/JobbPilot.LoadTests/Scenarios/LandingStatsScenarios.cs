// JobbPilot perf fitness function — GET /api/v1/landing/stats.
//
// Mäter mot ADR 0045 Beslut 1 klass (a) read-query/list:
//   p95 ≤ 300 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//   p99 600 ms (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — det är medvetet (ADR 0045 Beslut 1).
//
// ENDPOINT-PROFIL:
//   - Anonym, ingen Authorization-header
//   - Cache-Control: public, max-age=30 (proxies absorberar; CDN-Cache MISS i CI)
//   - Backend: IDistributedCache.GetAsync + static readonly Floor-fallback
//   - Worker (RefreshLandingStatsJob) skriver Redis var 5e min
//   - Rate-limit: LandingPublicReadPolicy = IP-partitionerad, 60/min fixed-window
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, inte gissning):
//   Per-IP rate-limit 60/min betyder att en single-source runner inte kan köra
//   50 RPS utan 429-storm. NBomber kan inte trivialt rotera socket-IP. Lösning:
//   sustained 1 RPS i 120s = 120 lyckade samples per scenario, vilket ligger
//   strikt under 60/min/IP-taket och ger en statistiskt stabil p95-distribution
//   (Tukey: n≥100 räcker för p95 ± rimligt konfidensintervall för observe-only
//   trend-signal Fas 1).
//
//   Höjning av rate-limit eller IP-rotation = miljö-config-fråga, inte scenario-
//   designval. När loadtest-jobbet körs mot ephemeral CI-API kan APPSETTINGS-
//   override sätta LandingPublicRead.PermitLimit till ett högre tal — men det
//   är CI-jobbets ansvar, inte scenariots.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace JobbPilot.LoadTests.Scenarios;

internal static class LandingStatsScenarios
{
    /// <summary>
    /// Klass (a) read-query/list — Klas-låst p95 ≤ 300 ms (ADR 0045 Beslut 1).
    /// </summary>
    public const int Class_A_P95_BudgetMs = 300;

    /// <summary>
    /// Klass (a) read-query/list — p99-observation-mål 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = 600;

    /// <summary>
    /// Cache-hit warm-path. PRIMÄR signal mot p95-budgeten.
    ///
    /// Pre-warmar Redis genom en första GET (warm-up i NBomber Init), sedan
    /// sustained-load under rate-limit-taket. Mäter p95/p99 på "happy path" där
    /// Worker har refreshat och Redis svarar.
    /// </summary>
    public static ScenarioProps CacheHitWarmPath(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("landing_stats_cache_hit", async _ =>
            {
                var request = Http.CreateRequest("GET", $"{baseUrl}/api/v1/landing/stats")
                    .WithHeader("Accept", "application/json");

                var response = await Http.Send(httpClient, request);

                // 429 räknas som FAIL i NBomber-mätningen — det är rate-limit-
                // kollision, inte en hot-path-mätning. Om vi ser 429 i resultatet
                // betyder det att lastformen kalibrerats fel mot LandingPublicRead.
                return response;
            })
            // WarmUp = lät endpointen pre-cache:a Redis-nyckeln. Mätningen efteråt
            // är "stable warm state" som motsvarar normal prod-trafik.
            .WithWarmUpDuration(TimeSpan.FromSeconds(5))
            .WithLoadSimulations(
                // 1 RPS sustained i 120s = 120 samples. Strikt under 60/min/IP
                // (50% av taket → headroom mot fönster-rotation). p95 vid n=120
                // = sample 114; tillräcklig granularitet för observe-only trend.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// Cache-miss cold-path. SECONDARY signal — verifierar att Floor-fallback
    /// håller budget även när Redis svarar tomt.
    ///
    /// VIKTIGT: Detta scenario kan inte trivialt rensa Redis från NBomber-sidan
    /// (det skulle kräva Redis-CLI-access eller en dev-bara test-endpoint som
    /// floods cache). I första iterationen mäter scenariot bara samma yta som
    /// hit-path utan WarmUp + lägre RPS → första requesten i fönstret KAN vara
    /// cache-miss, resten är hits. Det är medvetet en svagare signal denna fas.
    ///
    /// När en cache-control-test-hook finns (ADR 0064 follow-up?) kan denna
    /// scenario uppgraderas till en deterministisk cold-path-mätning.
    /// </summary>
    public static ScenarioProps CacheMissColdPath(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("landing_stats_cache_miss_indicator", async _ =>
            {
                var request = Http.CreateRequest("GET", $"{baseUrl}/api/v1/landing/stats")
                    .WithHeader("Accept", "application/json")
                    // Cache-busting query-param tvingar inte server-side cache-
                    // miss (handler ignorerar query-string), men hindrar
                    // potentiella mellanliggande proxies (CDN/ALB) från att
                    // svara cachat. Mäter handler-latens, inte proxy-cache.
                    .WithHeader("Cache-Control", "no-cache");

                var response = await Http.Send(httpClient, request);
                return response;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // Lägre RPS (0.5/s = 30/min) → strikt under taket, lämnar
                // headroom för parallell kör av båda scenarierna utan
                // 60/min/IP-kollision (totalt ≤ 90/min < 60+60 = 120/min taket
                // i två tidsfönster).
                //
                // OBS: parallell scenario-körning delar HttpClient → samma
                // socket → samma RemoteIpAddress på server-sidan → SAMMA
                // rate-limit-bucket. Därför hålls summan strikt under 60/min.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}
