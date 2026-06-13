// Jobbliggaren perf fitness function — per-option facet-counts (Fas E2c-endpoint).
//
// ADR 0067 Beslut 4 + senior-cto-advisor 2026-06-10 (Väg B): NBomber-gaten är
// BLOCKING "före per-option går live" — uppfylls PROCEDURELLT i E2c
// (backend → lokal mätning → p95 i PR-body → FÖRST därefter FE-live-wiring;
// E2c-architect §6). I Fas D1 var scenariot authored-men-parkerat (port-only,
// ingen route); Fas E2c (2026-06-11) reste endpointen och AKTIVERADE
// scenariot i Program.cs (selektor "facet-counts" / "all").
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
// ENDPOINT-PROFIL (Fas E2c — byggd):
//   - Auth-gated (till skillnad från anonyma landing-stats). Dedikerad
//     FacetCountsPolicy 30/10s per user (CTO VAL 1 2026-06-11 — least common
//     mechanism; IOptions-bunden i RateLimitingOptions.FacetCounts).
//   - Tung dimension OccupationGroup = GROUP BY på STORED shadow-column
//     occupation_group_concept_id över ~44k rader, ~400 yrkesgrupper i resultat.
//     Detta är den primära p95-signalen (värsta GROUP BY-kardinaliteten).
//   - Reflektion-väg: en facett kan beräknas med dimensionens egen filter-lista
//     tömd men ETT ANNAT aktivt filter kvar (SPOT-mekanik, se IJobAdSearchQuery
//     FacetCountsAsync-doc) → sekundärt scenario som mäter den vägen.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, inte gissning):
//   Fas E2c LÅST (CTO VAL 1 2026-06-11): endpointen kör dedikerad
//   FacetCountsPolicy 30 req/10s per user (fixed window) — INTE ListReadPolicy.
//   Aritmetik vid parallell-körning av båda scenarierna i samma user-bucket:
//   scenario 1 (1 RPS = 10 req/10s-fönster) + scenario 2 (0,5 RPS = 5 req/10s)
//   = 15 req/10s — strikt under 30/10s-taket, noll 429-förorening av p95
//   (D1-utkastets ListRead-antagande gav 90/min > 60/min = kalibrerings-fel;
//   löst strukturellt av policy-domen, CTO VAL 1-konsekvens (c)).
//   1 RPS i 120s = 120 lyckade samples, statistiskt stabil p95 (Tukey: n≥100
//   räcker för observe-only trend-signal Fas 1).
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

namespace Jobbliggaren.LoadTests.Scenarios;

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

    // Route-/query-kontraktet LÅST i Fas E2c (architect §1 + CTO 2026-06-11):
    //   GET /api/v1/job-ads/facet-counts
    //       ?dimension=<OccupationGroup|Municipality|Region>
    //       &occupationGroup=&municipality=&region=&q=
    // Endpointen rest i JobAdsEndpoints (FacetCountsPolicy 30/10s, private
    // no-store). D1-utkastets provisoriska form visade sig exakt.
    private const string FacetCountsPath = "/api/v1/job-ads/facet-counts";

    // Reellt region-concept-id för reflektion-scenariot (Stockholms län,
    // DB-verifierat 2026-06-11 mot taxonomy_concepts). Ersatte D1:s
    // PLACEHOLDER — ett reellt id ger en meningsfull reflektions-mängd.
    private const string ReflectedRegionConceptId = "CifL_Rzy_Mku";

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
    /// aktivt filter (region) kvar. Mäter SPOT-mekaniken (den facetterade
    /// dimensionens listor tömda, övriga filter kvar) i
    /// IJobAdSearchQuery.FacetCountsAsync. Reellt Stockholms-läns-id (E2c).
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
                    $"dimension=OccupationGroup&region={ReflectedRegionConceptId}");
                return response;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // 0,5 RPS = 5 req/10s-fönster; tillsammans med primär-scenariots
                // 10 req/10s = 15 req/10s i den delade user-bucketen — strikt
                // under FacetCountsPolicy 30/10s (CTO VAL 1, E2c-omkalibrering).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERAD i Fas E2c (2026-06-11): selektor-grenen "facet-counts" or "all"
// finns i perf/Jobbliggaren.LoadTests/Program.cs; route-/query-kontraktet låst
// (architect §1); LOADTEST_BEARER_TOKEN wireas per körning (lokalt: dev-test-
// kontot, se docs/runbooks/frontend-visual-verification.md cred-path).
//
// BudgetReporter emitterar ::warning:: vid p95-överskridande, exit 0
// ovillkorligt (observe-only Fas 1). Flip till BLOCKING gate (ADR 0067
// Beslut 4 "före live") = medveten Klas-GO-ratchet (ADR 0045 Beslut 6),
// aldrig en tyst default i denna fil.
// ─────────────────────────────────────────────────────────────────────────────
