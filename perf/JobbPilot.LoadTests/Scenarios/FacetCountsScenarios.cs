// JobbPilot perf fitness function — per-option facet-counts (PLANERAD Fas E-endpoint).
//
// ADR 0067 Beslut 4 + senior-cto-advisor 2026-06-10 (Väg B): NBomber-gaten är
// BLOCKING "före per-option går live". I Fas D1 levereras facet-counts som
// PORT-ONLY (IJobAdSearchQuery.FacetCountsAsync) — ingen Mediator-query, ingen
// endpoint. Mediator-query + endpoint tillkommer i Fas E. Därför är detta
// scenario AUTHORED men EJ AKTIVERAT: det är inte registrerat i Program.cs aktiva
// körlista, eftersom det inte finns någon route att träffa i D1 (en aktiv körning
// skulle ge connection-refused/404 → röd körning + falsk "perf-verifierad"-signal).
//
// >>> INGEN p95-DOM FALLER I D1. <<< Instrumentet existerar; mätningen sker först
// när Fas E reser endpointen och avkommenterar aktiveringen i Program.cs (se
// "FAS E-AKTIVERING" längst ned). Att läsa D1 som "perf verifierad" vore falsk-klar
// (CLAUDE.md §9.6, anti-falsk-klar).
//
// ─────────────────────────────────────────────────────────────────────────────
// BUDGET (verbatim ur ADR 0045 Beslut 1 — aldrig uppfunnen):
//   per-option facet-count = klass (a) read-query/list
//     p95 ≤ 300 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//     p99 600 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ENDPOINT-PROFIL (PLANERAD Fas E — INTE byggd än, allt nedan är provisoriskt):
//   - Auth-gated (till skillnad från anonyma landing-stats). Klass ListReadPolicy
//     eller egen facet-policy — rate-limit-tak EJ låst (CTO: tas med FE-vyn i E).
//   - Tung dimension OccupationGroup = GROUP BY på STORED shadow-column
//     occupation_group_concept_id över ~44k rader, ~400 yrkesgrupper i resultat.
//     Detta är den primära p95-signalen (värsta GROUP BY-kardinaliteten).
//   - Reflektion-väg: en facett kan beräknas med dimensionens egen filter-lista
//     tömd men ETT ANNAT aktivt filter kvar (SPOT-mekanik, se IJobAdSearchQuery
//     FacetCountsAsync-doc) → sekundärt scenario som mäter den vägen.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, inte gissning):
//   Auth-gated read → rate-limit per användare (ListReadPolicy ~60/min/user-klass,
//   EJ låst). Spegla landing-stats konservativa form: sustained låg RPS strikt
//   under det förväntade taket, single-source runner → en bucket. 1 RPS i 120s =
//   120 lyckade samples, strikt under 60/min/user × 2 fönster, statistiskt stabil
//   p95 (Tukey: n≥100 räcker för observe-only trend-signal Fas 1). Höjning av tak
//   eller token-rotation = miljö-config-fråga, inte scenario-designval.
//
// AUTH-HEADER (Fas E-aktiveringsdetalj, INTE ett scenario-designval):
//   Facet-counts kräver Authorization: Bearer <JWT> (auth-gated). Hur en test-JWT
//   förses är en MILJÖ-CONFIG-fråga för Fas E-körningen, inte detta scenario:
//   CI/lokala loadtest-jobbet sätter LOADTEST_BEARER_TOKEN (eller mintar en
//   test-JWT mot ephemeral API:s test-signing-key). Scenariot läser den env-varen
//   och sätter headern; saknas den loggas det och requests blir 401 (→ fail-count,
//   BudgetReporter-warning). Wiringen av token-källan görs i Fas E samtidigt som
//   endpointen reses — den är medvetet INTE hårdkodad här.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace JobbPilot.LoadTests.Scenarios;

internal static class FacetCountsScenarios
{
    /// <summary>
    /// Klass (a) read-query/list — Klas-låst p95 ≤ 300 ms (ADR 0045 Beslut 1).
    /// Återanvänder samma budget-konstant-mönster som <see cref="LandingStatsScenarios"/>;
    /// per-option facet-count är samma klass (a) (ny omätt hot-path, ADR 0067 Beslut 4).
    /// </summary>
    public const int Class_A_P95_BudgetMs = LandingStatsScenarios.Class_A_P95_BudgetMs;

    /// <summary>
    /// Klass (a) read-query/list — p99-observation-mål 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = LandingStatsScenarios.Class_A_P99_ObserveMs;

    // PROVISORISK route-konstant. Route-/query-kontraktet är INTE låst i D1 — det
    // tas med FE-vyn i Fas E (CTO 2026-06-10). Fas E justerar denna sträng till den
    // faktiska routen. Form speglar /api/v1-konventionen i landing-stats:
    //   GET /api/v1/job-ads/facet-counts
    //       ?dimension=<OccupationGroup|Municipality|Region>
    //       &occupationGroup=&municipality=&region=&q=
    private const string FacetCountsPath = "/api/v1/job-ads/facet-counts";

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön (Fas E-aktiveringsdetalj). Saknas
    /// den körs scenariot utan Authorization → 401 → fail-count → BudgetReporter-
    /// warning. Token-källans wiring görs i Fas E (miljö-config), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    // Bygger en facet-count-request mot den provisoriska routen och sätter
    // Authorization-headern om LOADTEST_BEARER_TOKEN finns (Fas E-detalj).
    // Returnerar NBomber-svaret (Response<HttpResponseMessage>) direkt så scenario-
    // lambdan kan returnera det som IResponse — exakt som LandingStats gör.
    private static Task<Response<HttpResponseMessage>> SendFacetRequestAsync(
        HttpClient httpClient, string baseUrl, string query)
    {
        var request = Http.CreateRequest("GET", $"{baseUrl}{FacetCountsPath}?{query}")
            .WithHeader("Accept", "application/json");

        var token = BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request = request.WithHeader("Authorization", $"Bearer {token}");
        }

        return Http.Send(httpClient, request);
    }

    /// <summary>
    /// PRIMÄR signal — tung dimension OccupationGroup, inga aktiva filter.
    /// GROUP BY occupation_group_concept_id över ~44k rader → ~400 yrkesgrupper.
    /// Värsta GROUP BY-kardinaliteten = den hot-path klass (a) p95 ska klä.
    /// </summary>
    public static ScenarioProps OccupationGroupHeavy(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("facet_counts_occupation_group", async _ =>
            {
                // Ingen aktiv filter-lista — bredaste GROUP BY-svaret (alla annonser).
                var response = await SendFacetRequestAsync(
                    httpClient, baseUrl, "dimension=OccupationGroup");

                // 401 (saknad/ogiltig token) eller 429 (rate-limit) räknas som FAIL
                // i NBomber-mätningen — inte en hot-path-mätning. BudgetReporter
                // flaggar non-zero fail separat så code-reviewer ser kalibrerings-
                // eller auth-wiring-miss.
                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(5))
            .WithLoadSimulations(
                // 1 RPS sustained i 120s = 120 samples, strikt under det förväntade
                // per-user-taket (~60/min). p95 vid n=120 = sample 114; tillräcklig
                // granularitet för observe-only trend.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// SEKUNDÄR signal — reflektion-vägen: facett mot OccupationGroup med ETT annat
    /// aktivt filter (region) kvar. Mäter SPOT-mekaniken (dimensionens egen lista
    /// tömd, övriga filter kvar) i IJobAdSearchQuery.FacetCountsAsync.
    ///
    /// Region-värdet nedan är ett PLACEHOLDER concept-id — Fas E byter till ett
    /// reellt id ur taxonomin (eller parametriserar via env). En tom/ogiltig
    /// region ger bara ett mindre resultat, inte ett fel — signalen håller.
    /// </summary>
    public static ScenarioProps ReflectedWithActiveFilter(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("facet_counts_occupation_group_reflected", async _ =>
            {
                // dimension=OccupationGroup beräknas med occupationGroup-listan tömd
                // men region-filtret aktivt → reflektion-vägen (SPOT bevarad).
                var response = await SendFacetRequestAsync(
                    httpClient,
                    baseUrl,
                    "dimension=OccupationGroup&region=PLACEHOLDER_REGION_CONCEPT_ID");
                return response;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // Lägre RPS (1 per 2s = 30/min) → headroom mot per-user-taket när
                // båda facet-scenarierna delar HttpClient/socket → samma bucket
                // (summa ≤ 90/min, strikt under två 60/min-fönster).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FAS E-AKTIVERING (gör INTE detta i D1 — endpointen finns inte än).
//
// När Fas E reser GET-endpointen, lås route-/query-kontraktet och token-källan,
// gör följande i perf/JobbPilot.LoadTests/Program.cs — exakt speglat mot hur
// landing-stats redan registreras i `if (scenarioSelector is "landing-stats" or "all")`:
//
//   1. Lägg till en selektor-gren (eller utöka "all"):
//
//        if (scenarioSelector is "facet-counts" or "all")
//        {
//            var facetHeavy     = FacetCountsScenarios.OccupationGroupHeavy(httpClient, baseUrl);
//            var facetReflected = FacetCountsScenarios.ReflectedWithActiveFilter(httpClient, baseUrl);
//
//            scenarios.Add(facetHeavy);
//            scenarios.Add(facetReflected);
//
//            scenarioBudgets[facetHeavy.ScenarioName]     = FacetCountsScenarios.Class_A_P95_BudgetMs;
//            scenarioBudgets[facetReflected.ScenarioName] = FacetCountsScenarios.Class_A_P95_BudgetMs;
//        }
//
//   2. Justera FacetCountsPath ovan till den låsta routen + query-formen.
//   3. Wira LOADTEST_BEARER_TOKEN (eller test-JWT-mint) i loadtest-jobbet/lokalt.
//
// BudgetReporter är redan generell (matchar scenarioBudgets-dicten) → ::warning::
// vid p95-överskridande, exit 0 ovillkorligt (observe-only Fas 1). Flip till
// BLOCKING gate (ADR 0067 Beslut 4 "före live") = medveten Klas-GO-ratchet
// (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
// ─────────────────────────────────────────────────────────────────────────────
