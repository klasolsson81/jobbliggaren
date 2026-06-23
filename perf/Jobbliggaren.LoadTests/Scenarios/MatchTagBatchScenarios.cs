// Jobbliggaren perf fitness function — POST /api/v1/me/job-ad-match-tags (F4-13/F4-15).
//
// ADR 0076 Decision 5 / ADR 0045 Beslut 1 klass (a) read-query/list:
//   p95 ≤ 300 ms (Klas-låst — produkt/UX/kostnad, Accepted 2026-05-17)
//   p99 600 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT-PROFIL (F4-13/F4-15 — uppdaterad med F4-15 DEK + CV-read-delta):
//   - Anonym-tolerant (INTE RequireAuthorization-gated). Handler returnerar tom
//     map utan UserId / utan angiven occupation (SSYK-gate). Om LOADTEST_BEARER_
//     TOKEN saknas → scenariot kör anonymt → handler short-circuit:ar → tom map
//     → 200 OK men latensen mäter INTE den autentiserade hot-path-vägen. Token
//     krävs för att mäta den meningsfulla handler-latensen. BudgetReporter flaggar
//     fail-count separat vid 429.
//   - DUAL-partition rate-limit: JobAdMatchBatchPolicy 60/min (user:-bucket om
//     auth, ip:-bucket annars). Parity JobAdStatusBatchPolicy (ADR 0063).
//   - Batch-validator cap = 100 IDs (GetJobAdMatchBatchQueryValidator).
//   - Handler F4-15 (bygger på F4-13): BuildFullForVerdictAsync (NY F4-15):
//       1. SELECT JobSeekers (AsNoTracking) → 1 DB round-trip.
//       2. currentDataOwner.SetOwner + dataKeyStore.GetOrCreateDataKeyAsync →
//          DEK-uppvärmning (scope-memoized; KMS-/LocalDataKeyProvider-anrop).
//          NY deltakostnad vs F4-13. Cached per request-scope (värsta fallet =
//          förstakallad per request, scope-cache hit därefter).
//       3. SELECT Resumes WHERE id = PrimaryResumeId WITH Include(Versions) →
//          FieldDecryptionMaterializationInterceptor dekrypterar Content (AES-256-GCM).
//          NY deltakostnad vs F4-13.
//       4. In-memory ISkillResolver.Resolve(Content.Skills[alla]) → concept-ids.
//       + ScoreFullBatchAsync (NY F4-15):
//       5. SELECT job_ads WHERE id = ANY(@ids) WITH extracted_terms projected →
//          1 round-trip inkl. ExtractedTerms (utökat vs F4-13:s ScoreBatchAsync).
//       6. In-memory ScoreConceptCoverage per rad (≤100) med hoisted concept-ids.
//     F4-13-banan (utan F4-15): 2 round-trips + in-memory Grade. F4-15 lägger till
//     DEK-warm + encrypted-content-read + resolver + ScoreConceptCoverage-overhead.
//     Worst-case för scenariot = kallt DEK (första request efter DEK-scope-init).
//
// ─────────────────────────────────────────────────────────────────────────────
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, ej gissning):
//   JobAdMatchBatchPolicy 60/min per user:-bucket (parity JobAdStatusBatchPolicy,
//   RateLimitingOptions.JobAdMatchBatch). Anonym ip:-bucket delas med alla anonyma
//   på samma loopback-IP — kör ALLTID med LOADTEST_BEARER_TOKEN för meningsfull
//   autentiserad mätning.
//
//   Standard-scenariot (NormalPageSize, 20 IDs): 0,5 RPS = 30 req/min = 50 % av
//   user:-bucket-taket (60/min). Headroom mot rate-limit-kollision vid parallell
//   körning med status-batch-scenariot (om "all"-selector), som delar user-bucket
//   med sin egen JobAdStatusBatchPolicy 60/min (parallella scenarion delar token
//   → samma partition → deras rates summeras). 0,5 RPS × 120s = 60 samples →
//   tillräcklig p95-signal (Tukey n≥30 för observe-only Fas 1; n=60 bättre
//   granularitet).
//
//   Stress-scenariot (StressCapSize, 100 IDs): lägre 0,25 RPS = 15 req/min = 25 %
//   av bucket-taket. Motivering: 100-ID-requester är tyngre per request (mer
//   = ANY-arbete + mer in-memory-scoring) men OCKSÅ större nätverkspayload per
//   request. Headroom bevaras. 0,25 RPS × 120s = 30 samples → statistisk gräns
//   för observe-only trend (Tukey: n=30 räcker med brett konfidensintervall Fas 1;
//   höj till 0,5 RPS om stress-scenariot körs isolerat med LOADTEST_SCENARIOS=
//   match-tags-stress).
//
// ─────────────────────────────────────────────────────────────────────────────
// FIXTURE-IDS (kalibrerings-notat):
//   Scenariot kör mot LOADTEST_BASE_URL med ett payload av Guid.NewGuid()-IDs
//   per default. En DB som inte innehåller dessa IDs returnerar ett tomt Entries-
//   map (handler:n omittar IDs som inte hittas / soft-deletade) — requestet är
//   200 OK men latensen mäter ENBART:
//     - BuildFromPreferencesAsync (SELECT JobSeekers, 1 row),
//     - ScoreBatchAsync: 1 SELECT mot job_ads WHERE id = ANY(@ids) → 0 rows.
//   Det är en KONSERVATIV mätning: tom batch är snabbare än en med 20/100 träffar
//   eftersom in-memory-scoring och MatchGradeCalculator.Grade-anropen skippas.
//
//   MENINGSFULL stress-mätning kräver seeded test-IDs (verkliga job_ads-rader
//   i loadtest-DB:n). Wira in via LOADTEST_JOB_AD_IDS (komma-separerade Guids)
//   i CI-miljöns docker-compose-setup — se kommentar i ScenarioJobAdIds-propertyn
//   nedan. Utan den kör scenariot mot tomma IDs och p95 mäter det konservativa
//   golvet (2 DB-round-trips utan scoring-overhead).
//
//   Prioritet: tom-ID-mätning är bättre än INGEN mätning (pipeline-overhead,
//   Mediator-pipeline, validation, auth-short-circuit mäts korrekt). Seeding
//   = Fas E-follow-up (samma väg som Facet-counts Bearer-token-wiring).
//
// ─────────────────────────────────────────────────────────────────────────────
// F4-15 MÄTKRAV (CTO R5 / CLAUDE.md §2.5):
//   Meningsfull worst-case-mätning av DEK-warm + full CV-read + ScoreConceptCoverage
//   kräver att testanvändaren (LOADTEST_BEARER_TOKEN) har:
//     (1) Primary CV med ≥1 Content.Skills (encrypted → DEK-warm aktiveras).
//     (2) LOADTEST_JOB_AD_IDS med verkliga job_ads-rader som har extracted_terms.
//   Utan (1): BuildFullForVerdictAsync returnerar tom skill-lista → ScoreConceptCoverage
//             NotAssessed → ingen skill-scoring overhead. Mäter DEK-warm + Versions-SELECT
//             men inte scoring. Delvis meningsfull.
//   Utan (2): tom-ID-fallback (se ovan). Mäter DEK-warm + CV-read men INTE ScoreConceptCoverage.
//   Korrekt instrument för F4-15-budgetdomen kräver (1)+(2). Utan dem rapporteras
//   signalen som "konservativt golv" (DEK-warm-overhead utan scoring), EJ full worst-case.
//   Denna deferral är parity F4-14:s honest-deferral-notat (dev-DB nere / ingen seeded fixture).

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using System.Text;
using System.Text.Json;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class MatchTagBatchScenarios
{
    /// <summary>
    /// Klass (a) read-query/list — Klas-låst p95 ≤ 300 ms (ADR 0045 Beslut 1).
    /// Återanvänder samma budget-konstant-mönster som <see cref="LandingStatsScenarios"/>;
    /// match-tag batch är en page-scoped read-overlay — klass (a) (parity ADR 0063 status batch).
    /// </summary>
    public const int Class_A_P95_BudgetMs = LandingStatsScenarios.Class_A_P95_BudgetMs;

    /// <summary>
    /// Klass (a) read-query/list — p99-observation-mål 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = LandingStatsScenarios.Class_A_P99_ObserveMs;

    // Route-kontraktet LÅST i F4-13 (ADR 0076 Decision 5):
    //   POST /api/v1/me/job-ad-match-tags
    //   Body: { "jobAdIds": ["<guid>", ...] }
    //   Response: { "entries": { "<guid>": { "grade": ..., ... } } }
    private const string MatchTagsPath = "/api/v1/me/job-ad-match-tags";

    // Representativt sid-sidantal per /jobb-sida (UX-kontraktet, ADR 0076 Decision 5):
    // FE skickar ≤20 IDs per sidladdning (default pageSize). Standard-payload.
    private const int NormalPageSize = 20;

    // Validator-cap (GetJobAdMatchBatchQueryValidator.MaxJobAdIdsPerCall = 100).
    // Stress-payload.
    private const int StressCapSize = 100;

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön. Saknas den kör scenariot anonymt
    /// → handler short-circuit:ar direkt (ingen autentiserad UserId) → tom map 200 OK.
    /// För att mäta den AUTENTISERADE hot-path-vägen (2 DB-round-trips + scoring)
    /// MÅSTE LOADTEST_BEARER_TOKEN sättas. Token-källans wiring görs i körnings-
    /// miljön (CI eller lokal dev-test-konto), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    /// <summary>
    /// Läser seeded test-IDs ur miljön (komma-separerade Guid-strängar) för en
    /// meningsfull mätning med faktiska job_ads-rader i loadtest-DB:n. Saknas
    /// LOADTEST_JOB_AD_IDS faller scenariot tillbaka på Guid.NewGuid()-payload
    /// (konservativ tom-ID-mätning — korrekt pipeline-signal, men inga Score-anrop).
    /// </summary>
    private static IReadOnlyList<string> ScenarioJobAdIds
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("LOADTEST_JOB_AD_IDS");
            if (string.IsNullOrWhiteSpace(raw))
            {
                // Fallback: generera Guids. Handler hittar inga IDs i DB → tom Entries.
                // Mäter: Mediator-pipeline + BuildFromPreferencesAsync (om autentiserad)
                // + ScoreBatchAsync:s 1-round-trip med 0 rows. Konservativ men inte meningslös.
                return [];
            }

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    // Bygg ett JSON-body med N IDs. Om ScenarioJobAdIds innehåller fler IDs än n
    // används de första n; annars genereras Guid.NewGuid()-IDs för resterande.
    private static StringContent BuildBody(int count)
    {
        var seedIds = ScenarioJobAdIds;
        var ids = new string[count];

        for (var i = 0; i < count; i++)
        {
            ids[i] = i < seedIds.Count
                ? seedIds[i]
                : Guid.NewGuid().ToString();
        }

        var json = JsonSerializer.Serialize(new { jobAdIds = ids });
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    // Bygger en match-tags-request och sätter Authorization-headern om token finns.
    // Returnerar NBomber-svaret (Response<HttpResponseMessage>) direkt.
    private static async Task<Response<HttpResponseMessage>> SendMatchTagsRequestAsync(
        HttpClient httpClient, string baseUrl, int idCount)
    {
        using var body = BuildBody(idCount);

        var request = Http.CreateRequest("POST", $"{baseUrl}{MatchTagsPath}")
            .WithHeader("Accept", "application/json")
            .WithBody(body);

        var token = BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request = request.WithHeader("Authorization", $"Bearer {token}");
        }

        // 429 (rate-limit-kollision) räknas som FAIL i NBomber → BudgetReporter
        // flaggar fail-count separat som ::warning:: (kalibrerings-miss, ej budget-signal).
        return await Http.Send(httpClient, request);
    }

    /// <summary>
    /// PRIMÄR signal — representativt sid-payload (20 IDs, FE:s standard-pageSize).
    ///
    /// Mäter den hot-path-vägen en /jobb-sida avfyrar: BuildFromPreferencesAsync
    /// (1 SELECT JobSeekers) + ScoreBatchAsync (1 SELECT job_ads WHERE id = ANY(@p))
    /// + ≤20 in-memory MatchGradeCalculator.Grade-anrop. Klass (a) p95 ≤ 300 ms
    /// (ADR 0045 Beslut 1 — verbatim, ej uppfunnen).
    ///
    /// Autentiserad mätning kräver LOADTEST_BEARER_TOKEN. Utan token → anonym →
    /// handler short-circuit:ar direkt → latensen mäter INTE 2 DB-round-trips
    /// (BudgetReporter-warning om fail-count visar 401; 200 OK men tom map = ingen
    /// fail-count — mätningen är tyst för svag om token saknas).
    /// </summary>
    public static ScenarioProps NormalPageSizeBatch(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("match_tags_batch_normal_page", async _ =>
            {
                var response = await SendMatchTagsRequestAsync(httpClient, baseUrl, NormalPageSize);
                return response;
            })
            // WarmUp: värmer DB-buffercache (PostgreSQL shared_buffers) + EF-query-plan-cache
            // (DbCommand compilation). p95-mätningen ska representera "normal warm-state prod-
            // trafik", inte cold-start. Parity FreeTextCountScenarios.QCountHotPath (10s warmup).
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 0,5 RPS = 30 req/min = 50 % av JobAdMatchBatchPolicy 60/min (user:-bucket).
                // Headroom mot rate-limit-kollision om scenariot körs parallellt med
                // status-batch-scenariot (delar user:-bucket via samma LOADTEST_BEARER_TOKEN).
                // 0,5 RPS × 120s = 60 samples → tillräcklig p95-granularitet (Tukey n≥30 Fas 1).
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// STRESS-signal — validator-capped payload (100 IDs, MaxJobAdIdsPerCall).
    ///
    /// Mäter det värsta fallet ADR 0045 klass (a) 300 ms-budgeten måste täcka:
    /// ScoreBatchAsync med 100 IDs i = ANY(@p) + 100 in-memory Grade-anrop. Stress-
    /// scenariot är en SECONDARY signal (primär = NormalPageSizeBatch). Kör isolerat
    /// med LOADTEST_SCENARIOS=match-tags-stress för renare bild.
    ///
    /// Lägre RPS (0,25/s = 15 req/min = 25 % av 60/min-taket) eftersom 100-ID-
    /// requester är tyngre per request och bevarar headroom mot 429 vid parallell
    /// körning. 0,25 RPS × 120s = 30 samples → statistisk nedre gräns för observe-
    /// only trend (brett konfidensintervall Fas 1, Tukey n=30).
    /// </summary>
    public static ScenarioProps StressCapBatch(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("match_tags_batch_stress_cap", async _ =>
            {
                var response = await SendMatchTagsRequestAsync(httpClient, baseUrl, StressCapSize);
                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 0,25 RPS = 15 req/min = 25 % av 60/min-taket. Headroom bevarad.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERING i Program.cs via selektor "match-tags" / "match-tags-stress" / "all".
// Se Program.cs-instruktionerna nedan för att registrera scenariot i selector-grenen.
//
// Körs med:
//   LOADTEST_SCENARIOS=match-tags LOADTEST_BEARER_TOKEN=<jwt> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
//   # Stress-only (100-ID-cap):
//   LOADTEST_SCENARIOS=match-tags-stress LOADTEST_BEARER_TOKEN=<jwt> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
//   # Med seeded IDs (meningsfull scoring-mätning):
//   LOADTEST_JOB_AD_IDS="<guid1>,<guid2>,..." LOADTEST_BEARER_TOKEN=<jwt> \
//   LOADTEST_SCENARIOS=match-tags \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
// BudgetReporter emitterar ::warning:: vid p95 > 300 ms, exit 0 ovillkorligt
// (observe-only Fas 1). Flip till BLOCKING gate = medveten Klas-GO-ratchet
// (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
// ─────────────────────────────────────────────────────────────────────────────
