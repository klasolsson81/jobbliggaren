using System.Net;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Polly;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091, senior-cto-advisor 2026-07-05) — pins the SCB population client's retry predicate.
/// The load-bearing invariant: <see cref="ScbRetryPolicy.ShouldRetry"/> extends the framework's default
/// transient handling (5xx / 408 / connection-level exceptions) but NEVER retries HTTP 429. SCB signals
/// overload with 429; retrying it amplifies calls toward a per-API-Id ban (a §12 STOPP condition) instead
/// of letting the circuit breaker absorb the pressure. The 429 → false case is the ban-risk-critical
/// assertion this class exists to lock in, so it is called out both as a first-class <see cref="FactAttribute"/>
/// and alongside the other status codes.
/// </summary>
public class ScbRetryPolicyTests
{
    [Fact]
    public void ShouldRetry_ReturnsFalseForHttp429_TheBanRiskExclusion()
    {
        // The single load-bearing case. Even though the framework's IsTransient would treat 429 as
        // retryable, ScbRetryPolicy fails fast: the guard short-circuits so a persistent 429 trips the
        // circuit breaker rather than adding rejected calls to the API-Id ban counter.
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        ScbRetryPolicy.ShouldRetry(Outcome.FromResult(response)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]   // 503 → framework-transient → retry
    [InlineData(HttpStatusCode.InternalServerError, true)]  // 500 → framework-transient → retry
    [InlineData(HttpStatusCode.RequestTimeout, true)]       // 408 → framework-transient → retry
    [InlineData(HttpStatusCode.TooManyRequests, false)]     // 429 → the deliberate exclusion → fail fast
    [InlineData(HttpStatusCode.OK, false)]                  // 200 → success is not transient → no retry
    public void ShouldRetry_MatchesFrameworkTransientHandling_ExceptItExcludes429(
        HttpStatusCode status,
        bool expectedRetry)
    {
        using var response = new HttpResponseMessage(status);

        ScbRetryPolicy.ShouldRetry(Outcome.FromResult(response)).ShouldBe(expectedRetry);
    }

    [Fact]
    public void ShouldRetry_ReturnsTrueForHttpRequestException_FrameworkTreatsItAsTransient()
    {
        // No Result on the outcome → the 429 guard passes (null != 429) and the framework classifies a
        // connection-level HttpRequestException as transient, so a genuine network blip is retried.
        var outcome = Outcome.FromException<HttpResponseMessage>(new HttpRequestException());

        ScbRetryPolicy.ShouldRetry(outcome).ShouldBeTrue();
    }
}
