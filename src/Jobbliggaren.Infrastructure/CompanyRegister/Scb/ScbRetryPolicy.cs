using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091, senior-cto-advisor 2026-07-05) — the retry predicate for the SCB population client.
/// Extends the framework's default transient handling (<see cref="HttpClientResiliencePredicates.IsTransient"/>
/// — 5xx, 408, timeouts, <c>HttpRequestException</c>) with ONE deliberate exclusion: it FAILS FAST on
/// HTTP 429 (Too Many Requests). SCB has explicitly signalled overload, so retrying just adds rejected
/// calls to the per-API-Id ban counter (a §12 STOPP condition) and masks the signal — the wrong move on
/// a metered/ban-risk integration (Nygard, <i>Release It!</i> — "Fail Fast" / do not retry into a rate
/// limit). A propagated 429 still counts toward the circuit breaker, so a persistent 429 trips it open
/// (5 min), which is the intended backpressure. Extracted from the inline resilience wiring in
/// <c>AddScbCompanyRegister</c> purely so this ban-risk-critical decision is unit-testable in isolation.
/// </summary>
internal static class ScbRetryPolicy
{
    /// <summary>
    /// True when the outcome should be retried: any framework-transient failure EXCEPT a 429, which is
    /// never retried (fail fast, let the circuit breaker absorb persistent overload).
    /// </summary>
    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
        && HttpClientResiliencePredicates.IsTransient(outcome);
}
