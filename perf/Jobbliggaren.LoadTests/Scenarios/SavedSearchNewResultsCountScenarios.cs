// Jobbliggaren perf fitness function — GET /api/v1/saved-searches/new-results-count (#312, ADR 0115).
//
// CONTEXT (ADR 0115 R1 / "Negative, accepted trade-offs"; TD-94):
//   GetNewSavedSearchResultsCountQueryHandler is a PER-SEARCH COUNT FAN-OUT: for each of the
//   caller's NotificationEnabled saved searches (capped at MaxSearchesScanned = 20, most-
//   recently-updated first), the handler calls IJobAdSearchQuery.CountNewSinceAsync once — the
//   exact shape that forced ListRecentSearchesQueryHandler's live per-search COUNT OFF in
//   production (Npgsql 57014, TD-94, an ADR 0045 budget break; ListRecentSearchesQueryHandler
//   hardcodes currentCount = 0 today unless IncludeCount is explicitly requested — see its
//   handler comments). TD-94 itself is CLOSED (root-fixed 2026-06-13: SET LOCAL
//   enable_seqscan=off bitmap-plan coax + a title-LIKE>=3 gate in the shared ApplyFilter,
//   measured warm for a SINGLE q-bearing COUNT: "ai" 15 ms, "utvecklare" 96 ms, "lärare" 116 ms —
//   see FreeTextCountScenarios.cs). CountNewSinceAsync inherits that fix via the same
//   CountWithBitmapPlanAsync helper (JobAdSearchQuery.cs). TD-94's fix caps the PER-COUNT cost;
//   it does NOT address FAN-OUT MULTIPLICATION. Up to 20 SEQUENTIAL counts — each opening its
//   own transaction (BEGIN + SET LOCAL + COUNT + COMMIT) — is an unmeasured amplification. ADR
//   0115 names this explicitly in its own "Negative / accepted trade-offs" section: "it is
//   unmeasured. It is therefore gated: CountNewSinceAsync MUST clear ADR 0045 (a fitness
//   function) before FE go-live." THIS file is that fitness function (R1).
//
//   The fan-out loop is sequential by design, not an oversight: the handler materializes the
//   search list BEFORE the loop specifically so "each CountNewSinceAsync opens its own short
//   transaction ... so no open-reader conflict on this [DbContext]" (handler XML doc) — a single
//   scoped DbContext cannot run concurrent queries. The measured latency below is genuinely the
//   SUM of up to 20 round trips, not a parallelized max. Whether that sum holds budget is exactly
//   what this scenario answers; if it does not, ADR 0115 already pre-specifies the evolution path
//   (lazy per-row client fetch, ADR 0060's useFacetCounts pattern, then only if still needed a
//   materialized read-model) — a design decision for dotnet-architect/senior-cto-advisor, not this
//   file.
//
// ─────────────────────────────────────────────────────────────────────────────
// BUDGET (verbatim from ADR 0045 Decision 1 — never invented):
//   List-shaped read query (a caller's saved-search badges) = class (a) read-query/list
//     p95 <= 300 ms (Klas-locked — product/UX/cost, Accepted 2026-05-17)
//     p99 600 ms   (observe-only Fas 1)
//
// Measurement point = server-side handler latency (LoggingBehavior-consistent). NBomber measures
// the HTTP round-trip from the in-process runner to the loopback API and back, which over
// loopback closely approximates handler latency (sub-ms loopback overhead). Edge-to-edge is NOT
// measured — deliberate (ADR 0045 Decision 1).
//
// ─────────────────────────────────────────────────────────────────────────────
// ENDPOINT PROFILE:
//   GET /api/v1/saved-searches/new-results-count — no query parameters, no request body. The
//   fan-out size is entirely SERVER-STATE-DRIVEN (how many NotificationEnabled SavedSearch rows
//   the caller owns) — there is no client-side lever to shape it, unlike e.g.
//   MatchTagBatchScenarios' request-body ID count. That is why this file's two scenarios differ
//   by ACCOUNT FIXTURE state, not by request shape (see FIXTURE PRECONDITIONS below).
//   - Auth-gated (RequireAuthorization on the route group, ADR 0005).
//   - ListReadPolicy: 60/min per UserId (TokenBucket) — the same policy as ListJobAds/
//     RunSavedSearch, applied here specifically because the fan-out is a multi-query-DoS surface
//     (SavedSearchesEndpoints.cs: "samma multi-query-DoS-yta som /run, x N sökningar").
//   - Handler shape (GetNewSavedSearchResultsCountQueryHandler):
//       1. SELECT JobSeekers (AsNoTracking, 1 row) — resolve JobSeekerId from the JWT subject.
//       2. SELECT SavedSearches WHERE JobSeekerId = ... AND NotificationEnabled
//          ORDER BY UpdatedAt DESC LIMIT 20 — ONE query, cheap, index-backed.
//       3. foreach search (<= 20): CountNewSinceAsync(criteria, ResultsSeenAt ?? CreatedAt) —
//          SEQUENTIAL, each its own transaction via CountWithBitmapPlanAsync. THIS loop is the
//          fan-out risk this fitness function exists to measure.
//   - A search's criteria WITHOUT Q never touches SearchVector/websearch_to_tsquery at all —
//     JobAdSearchComposition.ApplyFilter gates the entire FTS branch on
//     `!string.IsNullOrWhiteSpace(criteria.Q)` — cheap, index-backed concept-id filters only.
//     A search WITH Q hits the FTS/TOAST-detoast-mitigated branch (TD-94's fix, ~15-116 ms warm
//     for the measured terms above). A realistic notification-enabled population is a MIX of
//     both, not a monoculture of either extreme.
//
// ─────────────────────────────────────────────────────────────────────────────
// FIXTURE PRECONDITIONS (CTO discipline: calibrate against fact, never a guess):
//   Unlike every sibling scenario in this project, this endpoint takes NO query parameters — the
//   measured fan-out size is fixed entirely by how many NotificationEnabled SavedSearch rows
//   LOADTEST_BEARER_TOKEN's account owns. There is no request-shape lever (contrast
//   MatchTagBatchScenarios.BuildBody(count), which controls batch size client-side). Meaningful
//   measurement therefore requires the test account to be PROVISIONED, not merely authenticated —
//   parity MatchSortScenarios' "account must have a stated occupation preference" precondition and
//   MatchTagBatchScenarios' F4-15 "primary CV with >=1 skill" precondition. Wiring that
//   provisioning is a CI/local-calibration-environment concern (fixture seeding), not a scenario
//   design choice — same deferral as LOADTEST_JOB_AD_IDS / LOADTEST_OCCUPATION_GROUP_ID elsewhere
//   in this suite ("seeding = Fas E follow-up").
//
//   PRIMARY signal (FanOutCeiling scenario) — the R1-relevant number: provision
//   LOADTEST_BEARER_TOKEN's account with MaxFanOutSearches (20, mirrors
//   GetNewSavedSearchResultsCountQueryHandler.MaxSearchesScanned — the Application-layer SSOT;
//   this load-test project sits outside Jobbliggaren.sln per ADR 0045 Decision 4, so the cap is
//   mirrored here, not referenced) notification-enabled saved searches, with a REALISTIC MIX:
//   roughly 1/3 Q-bearing free-text criteria (reuse the TD-94-measured terms "ai"/"utvecklare"/
//   "lärare" for a fact-grounded worst case rather than an invented one) and roughly 2/3 plain
//   concept-id filters (OccupationGroup/Municipality/EmploymentType/WorktimeExtent — the cheaper,
//   FTS-free branch). A monoculture of 20 identical cheap filters would UNDERSTATE the worst case;
//   20 identical Q-bearing filters would OVERSTATE it beyond any plausible real user. This is a
//   reasoned assumption about a v1 feature's realistic distribution, not a measured production
//   fact — there is no production population yet (#312 is not FE-live).
//
//   SECONDARY signal (TypicalNotificationLoad scenario) — a realistic "active power user" account
//   with TypicalSearchCount (4) notification-enabled searches, a smaller, still-realistic fan-out
//   that isolates pipeline overhead plus a modest fan-out from the pathological 20-search ceiling.
//   Run in a SEPARATE invocation against a differently-provisioned account (same
//   LOADTEST_BEARER_TOKEN env-var name, a different token value) — mirrors MatchSortScenarios'
//   documented per-account-state run pattern (its footer's WorstCase/Typical vs Fallback-only
//   command blocks) rather than inventing a second token env var; the HTTP request itself is
//   IDENTICAL between the two scenarios below — only the target account's fixture differs.
//
//   WITHOUT either precondition, both scenarios still run and still measure something real
//   (Mediator pipeline + the 2 up-front SELECTs + however many searches the token's account
//   happens to already own — possibly 0) — a CONSERVATIVE, WEAKER signal, not a false one (parity
//   MatchTagBatchScenarios' "an empty-ID measurement beats no measurement"). BudgetReporter cannot
//   distinguish a fully-provisioned ceiling run from a 0-search account returning the same fast
//   200 OK — the trend JSON's ok/fail counts alone will not surface this; the operator running the
//   calibration owns verifying the fixture is actually in place before trusting a PASS.
//
// ─────────────────────────────────────────────────────────────────────────────
// LOAD CALIBRATION (CTO discipline: calibrate against fact, never a guess):
//   ListReadPolicy is a 60/min-per-UserId TokenBucket shared with ListJobAds/RunSavedSearch/
//   q-COUNT/match-sort when LOADTEST_SCENARIOS=all combines every ListReadPolicy-scoped scenario
//   in one run — the combined rate already exceeds the nominal 60/min in that mode (each sibling
//   scenario's own header notes this and recommends an isolated run for authoritative
//   calibration). This file follows the same documented discipline rather than pretending
//   "all" is perfectly balanced.
//
//   FanOutCeiling: 0.25 RPS (1 request / 4 s) = 15 req/min = 25% of the 60/min cap in isolation.
//   Conservative — this is the heaviest handler shape in the suite (up to 20 sequential DB round
//   trips per request, each its own transaction), so a lower rate leaves headroom against 429
//   contamination of the p95 signal. 0.25 RPS x 120 s = 30 samples — a Tukey-adequate lower bound
//   for an observe-only Fas 1 trend signal (n>=30, wide confidence interval).
//
//   TypicalNotificationLoad: 0.5 RPS (1 request / 2 s) = 30 req/min = 50% of the cap in isolation
//   — parity MatchTagBatchScenarios.NormalPageSizeBatch's rate reasoning for a lighter handler
//   shape (4 sequential counts vs up to 20). 0.5 RPS x 120 s = 60 samples, better p95 granularity
//   than the ceiling scenario.
//
//   Raise either rate if run isolated via LOADTEST_SCENARIOS=saved-search-notify or
//   =saved-search-notify-typical rather than combined under "all".

using NBomber.Contracts;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Jobbliggaren.LoadTests.Scenarios;

internal static class SavedSearchNewResultsCountScenarios
{
    /// <summary>
    /// Class (a) read-query/list — Klas-locked p95 &lt;= 300 ms (ADR 0045 Decision 1). Reuses the
    /// same budget-constant pattern as <see cref="LandingStatsScenarios"/>; the saved-search
    /// new-results-count fan-out is class (a) (ADR 0115 R1 — a new, unmeasured hot path).
    /// </summary>
    public const int Class_A_P95_BudgetMs = LandingStatsScenarios.Class_A_P95_BudgetMs;

    /// <summary>
    /// Class (a) read-query/list — p99 observe-only target 600 ms (observe-only).
    /// </summary>
    public const int Class_A_P99_ObserveMs = LandingStatsScenarios.Class_A_P99_ObserveMs;

    // Route contract (#312, ADR 0115): GET /api/v1/saved-searches/new-results-count. No query
    // parameters, no request body — see FIXTURE PRECONDITIONS above for why that matters.
    private const string NewResultsCountPath = "/api/v1/saved-searches/new-results-count";

    // Mirrors GetNewSavedSearchResultsCountQueryHandler.MaxSearchesScanned (a private const in
    // Jobbliggaren.Application — this load-test project is deliberately outside Jobbliggaren.sln,
    // ADR 0045 Decision 4, so it cannot reference that constant directly). SSOT lives in the
    // handler; keep this mirror in lockstep if that cap ever changes.
    public const int MaxFanOutSearches = 20;

    // A realistic "active power user" notification-enabled count for the secondary signal — well
    // under the pathological cap, still meaningfully above the "one search" case this fitness
    // function exists to avoid measuring (a single search would hide the fan-out entirely).
    public const int TypicalSearchCount = 4;

    /// <summary>
    /// Reads an optional Bearer token from the environment. Without it the scenario runs
    /// unauthenticated -&gt; 401 -&gt; fail-count -&gt; a separate BudgetReporter warning (a
    /// calibration gap, not a hot-path signal). Token wiring is a runtime-environment concern (CI
    /// or a local dev-test account), not a scenario design choice.
    /// </summary>
    private static string? BearerToken =>
        Environment.GetEnvironmentVariable("LOADTEST_BEARER_TOKEN");

    // Builds a new-results-count request and sets the Authorization header if a token is present.
    // Returns the NBomber response (Response<HttpResponseMessage>) directly, exactly as every
    // sibling scenario file does.
    private static Task<Response<HttpResponseMessage>> SendNewResultsCountRequestAsync(
        HttpClient httpClient, string baseUrl)
    {
        var request = Http.CreateRequest("GET", $"{baseUrl}{NewResultsCountPath}")
            .WithHeader("Accept", "application/json");

        var token = BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request = request.WithHeader("Authorization", $"Bearer {token}");
        }

        // 401 (missing/expired token) or 429 (rate-limit collision) count as FAIL in the NBomber
        // measurement; BudgetReporter flags a non-zero fail-count as its own ::warning:: separate
        // from the p95 budget check.
        return Http.Send(httpClient, request);
    }

    /// <summary>
    /// PRIMARY signal — the R1-relevant number. Measures the handler's per-search COUNT fan-out
    /// at its documented ceiling (<see cref="MaxFanOutSearches"/> = 20, ADR 0115's own bound).
    /// This is the exact question ADR 0115's "Negative / accepted trade-offs" section leaves
    /// open: "it is unmeasured. It is therefore gated: CountNewSinceAsync MUST clear ADR 0045
    /// ... before FE go-live." This scenario is that gate's instrument.
    ///
    /// Requires LOADTEST_BEARER_TOKEN for an account provisioned at the fan-out ceiling — see
    /// FIXTURE PRECONDITIONS above. Without it, this measures a conservative floor (however many
    /// notification-enabled searches the token's account already has), not the worst case.
    /// </summary>
    public static ScenarioProps FanOutCeiling(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("saved_search_new_results_count_fanout_ceiling", async _ =>
            {
                var response = await SendNewResultsCountRequestAsync(httpClient, baseUrl);
                return response;
            })
            // Longer warm-up than most class (a) scenarios (parity MatchSortScenarios.WorstCase,
            // 15 s): up to 20 sequential transactional COUNTs per request needs more
            // shared_buffers + query-plan-cache warmth than a single-query handler before the p95
            // reflects steady warm-state production traffic rather than cold-start variance.
            .WithWarmUpDuration(TimeSpan.FromSeconds(15))
            .WithLoadSimulations(
                // 0.25 RPS = 15 req/min = 25% of ListReadPolicy's 60/min cap in isolation.
                // Conservative — the heaviest handler shape in this suite. 0.25 RPS x 120 s =
                // 30 samples, a Tukey-adequate lower bound for an observe-only Fas 1 trend
                // (n>=30, wide confidence interval). Raise to 0.5 RPS if run isolated via
                // LOADTEST_SCENARIOS=saved-search-notify.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(4),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }

    /// <summary>
    /// SECONDARY signal — a realistic "active power user" fan-out
    /// (<see cref="TypicalSearchCount"/> = 4 notification-enabled searches), well below the
    /// pathological ceiling. Isolates pipeline overhead plus a modest, plausible fan-out from the
    /// worst case <see cref="FanOutCeiling"/> measures, giving code-reviewer/CTO a lower-bound
    /// data point alongside the ceiling.
    ///
    /// The request is IDENTICAL to <see cref="FanOutCeiling"/> — this endpoint has no
    /// client-side lever to shape the fan-out (see ENDPOINT PROFILE above). Run in a SEPARATE
    /// invocation against a differently-provisioned account (same LOADTEST_BEARER_TOKEN env-var
    /// name, a different token value) — mirrors MatchSortScenarios' documented
    /// per-account-state run pattern.
    /// </summary>
    public static ScenarioProps TypicalNotificationLoad(HttpClient httpClient, string baseUrl)
    {
        var scenario = Scenario.Create("saved_search_new_results_count_typical", async _ =>
            {
                var response = await SendNewResultsCountRequestAsync(httpClient, baseUrl);
                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // 0.5 RPS = 30 req/min = 50% of the cap in isolation — parity
                // MatchTagBatchScenarios.NormalPageSizeBatch's rate reasoning for a lighter
                // handler shape (4 sequential counts vs up to 20). 0.5 RPS x 120 s = 60 samples,
                // better p95 granularity than the ceiling scenario.
                Simulation.Inject(
                    rate: 1,
                    interval: TimeSpan.FromSeconds(2),
                    during: TimeSpan.FromSeconds(120)));

        return scenario;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AUTHORED-BUT-PARKED (parity ADR 0067's facet-counts scenario in its Fas D1 state — see
// FacetCountsScenarios.cs header). Registers via selector "saved-search-notify" (FanOutCeiling)
// and "saved-search-notify-typical" (TypicalNotificationLoad) in Program.cs, or "all". Neither
// selector is wired into CI's actual invocation yet (build.yml's loadtest job runs
// LOADTEST_SCENARIOS=baseline-only today — see Program.cs header) — this scenario activates once
// an ephemeral API stack exists in CI, exactly like every other hot-path scenario in this
// project.
//
// R1 (ADR 0115 / this endpoint's own handler doc): this fitness function MUST clear ADR 0045
// BEFORE the FE surface goes live (a separate Klas-gated PR, per ADR 0115 Decision "(2) FE =
// /sokningar re-activation"). Run it locally against a provisioned dev/ephemeral account before
// that PR ships — "authored" is not "cleared."
//
// Run with:
//   LOADTEST_SCENARIOS=saved-search-notify LOADTEST_BEARER_TOKEN=<jwt-at-20-cap> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
//   # Typical-only, a different account provisioned with ~4 notification-enabled searches:
//   LOADTEST_SCENARIOS=saved-search-notify-typical LOADTEST_BEARER_TOKEN=<jwt-typical> \
//     dotnet run --project perf/Jobbliggaren.LoadTests -c Release
//
// BudgetReporter emits ::warning:: on p95 overshoot, exit 0 unconditionally (observe-only Fas 1).
// Flip to a BLOCKING gate = a deliberate Klas-GO ratchet (ADR 0045 Decision 6), never a silent
// default in this file.
// ─────────────────────────────────────────────────────────────────────────────
