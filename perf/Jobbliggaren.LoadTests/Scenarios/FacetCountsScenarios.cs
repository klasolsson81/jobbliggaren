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
//   - #2199 (the occupation-name axis): dimension=OccupationName is a THIRD
//     GROUP BY form. Ads are counted per (occupation_name, occupation_group)
//     pair — not a shadow column — and the pairs are folded through the
//     occupation-name lineage in textResolver.CountOccupationNames
//     (JobAdSearchQuery.FacetCountsAsync).
//     ADR 0067 Beslut 4: the NBomber measurement against ADR 0045 is BLOCKING
//     before per-option goes live; the Testcontainers reading in ADR 0067
//     Amendment 2026-10-24 point 5 is a different measuring point (no HTTP
//     pipeline, no rate limiter). Two scenarios:
//       * OccupationNameHeavy — no filters, the widest pair set.
//       * OccupationNameWithPicks — the same facet with five occupationName= on
//         the wire, the request shape the FE sends when the user has already
//         picked occupation names. ExcludeDimension clears BOTH Yrke lists
//         (occupationGroup and occupationName) for the OccupationName facet, so
//         the picks do NOT narrow the WHERE: the difference from Heavy is the
//         cost of the request shape (binding, validator, criteria, SPOT
//         clearing), not a narrower GROUP BY.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, inte gissning):
//   The endpoint runs the dedicated FacetCountsPolicy (CTO VAL 1 2026-06-11) —
//   NOT ListReadPolicy. Since the retune of 2026-06-24 it is a TokenBucket, not
//   a fixed window: PermitLimit 30 / WindowSeconds 10 / SegmentsPerWindow 6 = 5
//   tokens per 10/6 s, i.e. a sustained 30 req/10s (3 req/s) per user and a
//   burst cap of 30. All scenarios share ONE user bucket (one Bearer token);
//   /remote-count is on the same policy.
//   Arithmetic when all four scenarios overlap (the first 120 s):
//     facet_counts_occupation_group             1 RPS   = 10 req/10s
//     facet_counts_occupation_group_reflected   0.5 RPS =  5 req/10s
//     facet_counts_occupation_name              0.5 RPS =  5 req/10s
//     facet_counts_occupation_name_picks        0.5 RPS =  5 req/10s
//                                                         ───────────
//                                                         25 req/10s
//   = 5 below the 30/10s cap → zero 429 contamination of p95. The 5 req/10s
//   left over are the margin for the bucket's other callers: run against a
//   dedicated test account, not an account someone is using in the UI at the
//   same time. After 120 s only the two OccupationName scenarios remain
//   (10 req/10s).
//   (D1-utkastets ListRead-antagande gav 90/min > 60/min = kalibrerings-fel;
//   löst strukturellt av policy-domen, CTO VAL 1-konsekvens (c)).
//   Samples: 1 RPS for 120s = 120 successful samples; 0.5 RPS for 240s = 120
//   samples for each of the OccupationName scenarios (reflected: 0.5 RPS for
//   120s = 60 samples, secondary signal). Statistically stable p95 (Tukey:
//   n≥100 suffices for an observe-only trend signal in Fas 1).
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
    // Extended in #2199: dimension=OccupationName and repeated &occupationName=
    // <concept-id> (string[]? occupationName in JobAdsEndpoints).
    private const string FacetCountsPath = "/api/v1/job-ads/facet-counts";

    // Reellt region-concept-id för reflektion-scenariot (Stockholms län,
    // DB-verifierat 2026-06-11 mot taxonomy_concepts). Ersatte D1:s
    // PLACEHOLDER — ett reellt id ger en meningsfull reflektions-mängd.
    private const string ReflectedRegionConceptId = "CifL_Rzy_Mku";

    // Five occupation names for OccupationNameWithPicks, from occupation-name-
    // lineage.json (Infrastructure/Taxonomy). All have currentIn ⊇ ["31"] and an
    // empty replacedByConceptIds (verified 2026-10-25): current names, none
    // replaced that would expand via the lineage. Four Projektledare in three
    // occupation groups (el and telekom share a group) + one nurse occupation
    // name in a fourth.
    private static readonly string[] PickedOccupationNameConceptIds =
    [
        "z5AM_ayf_WcL", // Projektledare, IT
        "1jm3_XFQ_TdJ", // Projektledare, el
        "T55e_efV_Eqh", // Projektledare, telekom
        "pTTz_5DC_itW", // Projektledare, bygg och anläggning
        "bXNH_MNX_dUR", // Sjuksköterska, grundutbildad
    ];

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
                // 1 RPS sustained for 120s = 120 samples = 10 req/10s of the
                // FacetCountsPolicy cap of 30/10s (the sum over all scenarios:
                // LAST-KALIBRERING in the file header). p95 at n=120 = sample 114;
                // sufficient granularity for the observe-only trend.
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
                // 0.5 RPS = a 5 req/10s window in the shared user bucket. The sum
                // over all four scenarios = 25 req/10s against FacetCountsPolicy
                // 30/10s (CTO VAL 1; LAST-KALIBRERING in the file header).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// #2199 — the occupation-name facet, no active filters. Not a shadow column's
    /// GROUP BY like the other dimensions: ads are counted per (occupation_name,
    /// occupation_group) pair and folded through the lineage in CountOccupationNames
    /// (JobAdSearchQuery.FacetCountsAsync). It is the measurement ADR 0067 Beslut 4
    /// requires before per-option goes live on /jobb.
    /// </summary>
    public static ScenarioProps OccupationNameHeavy(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("facet_counts_occupation_name", async _ =>
            {
                // No active filter list — the widest pair set (all ads with an
                // occupation name). 401/429 count as FAIL, as in the primary scenario.
                var response = await SendFacetRequestAsync(
                    httpClient, baseUrl, "dimension=OccupationName");
                return response;
            })
            // Without warm-up, like reflected: warm-up would spend bucket tokens
            // outside the LAST-KALIBRERING arithmetic. The first request may carry
            // a cold start; at n=120 it sits in the tail above p95 (sample 114).
            .WithoutWarmUp()
            .WithLoadSimulations(
                // 0.5 RPS = a 5 req/10s window in the shared user bucket (sum 25
                // req/10s, LAST-KALIBRERING in the file header). 240s gives 120
                // samples; p95 at n=120 = sample 114, the same granularity as the
                // primary scenario.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(240)));

        return scenario;
    }

    /// <summary>
    /// #2199 — the occupation-name facet with five picked occupation names (repeated
    /// occupationName=), the request shape the FE sends when the user has already
    /// picked occupation names. ExcludeDimension clears the picks out of the WHERE
    /// (both Yrke lists), so the DB work is the same as <see cref="OccupationNameHeavy"/>;
    /// the difference from that scenario is the cost of the request shape. Five ids
    /// from the lineage artefact: current names (currentIn ⊇ "31"), see
    /// PickedOccupationNameConceptIds.
    /// </summary>
    public static ScenarioProps OccupationNameWithPicks(HttpClient httpClient, string baseUrl)
    {
        // Built once, not per request. The same repeated form as the FE's
        // getFacetCounts (params.append("occupationName", v) per value).
        var query = "dimension=OccupationName&" + string.Join(
            "&",
            PickedOccupationNameConceptIds.Select(
                id => $"occupationName={Uri.EscapeDataString(id)}"));

        var scenario = Scenario.Create("facet_counts_occupation_name_picks", async _ =>
            {
                var response = await SendFacetRequestAsync(httpClient, baseUrl, query);
                return response;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // 0.5 RPS for 240s = 5 req/10s and 120 samples, like OccupationNameHeavy
                // (sum over all four scenarios = 25 req/10s, LAST-KALIBRERING in the
                // file header).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(240)));

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
