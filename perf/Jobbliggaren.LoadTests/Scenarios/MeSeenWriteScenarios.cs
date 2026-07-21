// Jobbliggaren perf fitness function — POST /api/v1/me/jobs/seen command/write hot path.
//
// KONTEXT (#753, epik #737 — audit 2026-07-10, finding m3-write-budget-unmeasured):
//   ADR 0045 låser fyra budget-klasser men NBomber-harnessen hade före denna fil ENBART
//   klass (a)-scenarier. Klass (c) command/write — CQRS-skrivvägen (Result<T>, UnitOfWork-
//   behavior) — hade noll instrument. Detta scenario stänger den luckan så en framtida
//   ratchet (ADR 0045 Beslut 6) har en signal.
//
// ─────────────────────────────────────────────────────────────────────────────
// BUDGET (verbatim ur ADR 0045 Beslut 1 — aldrig uppfunnen):
//   POST /me/jobs/seen dispatchar MarkJobsSeenCommand → klass (c) command/write
//     p95 ≤ 400 ms (CTO-tekniskt satt)
//     p99 800 ms   (observe-only Fas 1)
//
// Mätpunkt = server-side handler-latens (LoggingBehavior-konsekvent). NBomber
// mäter HTTP-round-trip från in-process runner → loopback API → response, vilket
// över loopback approximerar handler-latensen tätt (sub-ms loopback-overhead).
// Edge-to-edge mäts INTE — medvetet (ADR 0045 Beslut 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// VAL AV WRITE-YTA (senior-cto-advisor 2026-07-21, BESLUT 1 — mätvärde slår ordalydelse):
//   Klass (c) mäter en GENUIN command/write genom hela pipelinen (Logging → Validation →
//   Authorization → UnitOfWork). POST /me/jobs/seen valdes över SaveJobAd (no-op efter
//   sample 1 + audit-rad + fixtur-beroende jobAdId) och consent-PUT:arna (issue-kroppens
//   bokstavliga "PUT-flagg-toggle", men IAuditableCommand → audit-rad + Art. 7(1)-immutabel
//   consent-evidens per sample → förorenar den delade dev-DB:ns rättsliga bevisspår, §5/§6.5).
//   MarkJobsSeenCommand är MEDVETET icke-IAuditableCommand (GDPR data-minimisation,
//   MarkJobsSeenCommand.cs) → ingen audit-rad, ingen radackumulering, ingen PII.
//
//   NO-OP-FÄLLAN (varför tom body, INTE ett fast seenThrough): SetLastSeenJobs
//   (JobSeeker.cs) no-op:ar när seenThrough <= nuvarande watermark. Ett FAST värde skulle
//   därför skriva en gång och sedan mäta en händelselös pass-through (dirty-fri SaveChanges)
//   → en VAKUÖS gate som blir grön av fel skäl. Vi skickar därför tom/null body: handlern
//   faller tillbaka på clock.UtcNow (MarkJobsSeenCommandHandler.cs), som mellan två samples
//   (4 s isär) alltid är > current → aggregatet blir dirty → en GENUIN en-radig UPDATE via
//   UnitOfWorkBehavior på VARJE sample. Det är exakt klass (c):s skrivpipeline.
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT-PROFIL:
//   - POST /api/v1/me/jobs/seen (MeJobsEndpoints.cs). Owner-scoped mutation.
//   - Auth-gated (RequireAuthorization). LOADTEST_BEARER_TOKEN krävs — samma mekanism
//     som de authade läs-scenarierna. Saknas token → 401 → fail-count → BudgetReporter-
//     warning. Saknar token-användaren en JobSeeker-rad → NotFound → fail-count (synligt).
//   - MeWritePolicy: FixedWindow 30/60s per UserId (claim "sub", RateLimitingOptions.MeWrite).
//   - Body { seenThrough } nullable; tom body ({}) → null → handlern faller tillbaka på
//     clock.UtcNow. Svar 204 (No Content) / 400.
//   - Accepterad trade-off (CTO): mäter en LÄTT write (en-kolumns UPDATE), inte en tung
//     command+audit-write. För observe-only Fas 1 är en ren, repeterbar p95-trend på skriv-
//     pipelinen exakt vad klass (c) behöver.
//
// LAST-KALIBRERING (CTO-disciplin: kalibrera mot fakta, ej gissning):
//   MeWritePolicy 30/60s per UserId. En total-rate på 0,25 RPS (1 request var 4:e sekund)
//   = 15 req/min = 50 % av taket → headroom mot 429 vid parallell körning i "all" (delar
//   user-bucket med andra MeWrite-ytor om sådana scenarier tillkommer). 0,25 RPS × 120s =
//   30 samples → statistisk nedre gräns för observe-only trend (Tukey n≥30 med brett
//   konfidensintervall Fas 1). Höj during till 180–240s för finare granularitet om bara
//   detta scenario körs isolerat med LOADTEST_SCENARIOS=me-seen-write.

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using System.Text;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class MeSeenWriteScenarios
{
    /// <summary>
    /// Klass (c) command/write — CTO-tekniskt satt p95 ≤ 400 ms (ADR 0045 Beslut 1, verbatim).
    /// Klass (c) får sin första kanoniska ägare här (samma mönster som
    /// <see cref="LandingStatsScenarios"/> äger klass (a)); framtida syskon-scenarier av
    /// klass (c) re-exporterar via <c>= MeSeenWriteScenarios.Class_C_P95_BudgetMs</c>.
    /// </summary>
    public const int Class_C_P95_BudgetMs = 400;

    /// <summary>
    /// Klass (c) command/write — p99-observation-mål 800 ms (observe-only Fas 1).
    /// Dokumentär: BudgetReporter jämför enbart p95 mot budget (BudgetReporter.cs).
    /// </summary>
    public const int Class_C_P99_ObserveMs = 800;

    private const string SeenPath = "/api/v1/me/jobs/seen";

    // Tom JSON-body → MarkJobsSeenRequest(SeenThrough: null) → handlern faller tillbaka på
    // clock.UtcNow → garanterad monoton advance (dirty entity) per sample. Se NO-OP-FÄLLAN
    // i fil-headern: ett fast värde hade no-op:at efter första anropet (vakuös gate).
    private const string EmptyJsonBody = "{}";

    /// <summary>
    /// Läser en valfri Bearer-token ur miljön. Saknas den körs scenariot utan
    /// Authorization → 401 → fail-count → BudgetReporter-warning. Token-källans
    /// wiring görs i körnings-miljön (CI eller lokal dev-test-konto,
    /// se docs/runbooks/frontend-visual-verification.md cred-path), inte här.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    /// <summary>
    /// PRIMÄR signal — command/write hot path (watermark-advance på varje sample).
    ///
    /// Mäter handler-latens för MarkJobsSeenCommand genom hela CQRS-skrivpipelinen
    /// (Validation → Authorization → UnitOfWork → en-radig UPDATE) mot ADR 0045 klass (c)
    /// 400 ms-budgeten. En regression i skriv-pipelinens overhead (behavior-kedjan,
    /// change-tracking, SaveChanges) fångas som p95-överskridande.
    /// </summary>
    public static ScenarioProps SeenWatermarkWrite(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("me_jobs_seen_write", async _ =>
            {
                using var body = new StringContent(EmptyJsonBody, Encoding.UTF8, "application/json");

                var request = Http.CreateRequest("POST", $"{baseUrl}{SeenPath}")
                    .WithHeader("Accept", "application/json")
                    .WithBody(body);

                var token = BearerToken;
                if (!string.IsNullOrWhiteSpace(token))
                    request = request.WithHeader("Authorization", $"Bearer {token}");

                // 401 (saknad token) / 404 (token-användare utan JobSeeker) / 429 (rate-limit)
                // räknas som FAIL i NBomber och visas av BudgetReporter som en separat
                // ::warning:: — kalibreringsfel, inte hot-path-signal.
                return await Http.Send(httpClient, request);
            })
            // Ingen WarmUp: skriv-pathen har ingen cache att värma på det sätt en läs-query
            // har (varje anrop är en genuin UPDATE). Change-tracking/SaveChanges-overhead är
            // vad klass (c) mäter — det ska inte döljas av en uppvärmning.
            .WithoutWarmUp()
            .WithLoadSimulations(
                // 0,25 RPS = 15 req/min = 50 % av MeWritePolicy 30/60s-taket → headroom mot
                // 429. 0,25 RPS × 120s = 30 samples → statistisk nedre gräns för observe-only
                // trend (Tukey n≥30 Fas 1). Höj during till 180–240s om körd isolerat.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AKTIVERAS via selektor "me-seen-write" eller "all" i Program.cs.
// LOADTEST_BEARER_TOKEN wireas per körning (lokalt: dev-test-kontot,
// se docs/runbooks/frontend-visual-verification.md cred-path).
//
// VIKTIGT: write-scenariot MUTERAR mål-DB:ns dev-test-användar-watermark. Kör ALDRIG
// mot den delade dev-DB:n medan en annan session äger stacken (§6.5) — endast ensam
// stack-owner. Mutationen är hygienisk (en-kolumns monoton advance, ingen audit-rad,
// ingen radackumulering), men den delade DB:n har EN ägare i taget.
//
// BudgetReporter emitterar ::warning:: vid p95 > 400 ms, exit 0 ovillkorligt
// (observe-only Fas 1). Flip till BLOCKING gate = medveten Klas-GO-ratchet
// (ADR 0045 Beslut 6), aldrig en tyst default i denna fil.
//
// Körs med:
//   LOADTEST_SCENARIOS=me-seen-write LOADTEST_BEARER_TOKEN=<jwt> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
// ─────────────────────────────────────────────────────────────────────────────
