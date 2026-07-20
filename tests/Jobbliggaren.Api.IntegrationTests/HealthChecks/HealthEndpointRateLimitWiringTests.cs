using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.HealthChecks;

/// <summary>
/// #483 Low — the wiring pin for the anonymous health endpoints, measured from the BUILT endpoint
/// graph (the <c>CompaniesRateLimitWiringTests</c> pattern — stronger than a key-string pin: a
/// dropped <c>.RequireRateLimiting</c> silently removes the DoS bulkhead while every options test
/// stays green). Both <c>/api/live</c> and <c>/api/ready</c> must carry <c>HealthCheckPolicy</c>:
/// <c>/api/ready</c> runs a Postgres CanConnect + Redis PING per hit (an amplification vector for
/// an unauth flood), and <c>/api/live</c>, though cheap, is still an anonymous surface.
///
/// <para>
/// Health endpoints carry no HTTP-method constraint (MapHealthChecks answers any method), so the
/// match is on route pattern only — unlike the method-scoped companies pins.
/// </para>
/// </summary>
[Collection("Api")]
public class HealthEndpointRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Theory]
    [InlineData("/api/live")]
    [InlineData("/api/ready")]
    public void Each_health_endpoint_carries_the_health_check_rate_limit_policy(string route)
    {
        _ = _factory.CreateClient();

        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(
                e.RoutePattern.RawText, route, StringComparison.Ordinal))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull(
            $"{route} must carry .RequireRateLimiting(...) — an anonymous unauth surface without a "
            + "rate-limit is a DoS amplification vector (/api/ready hits Postgres + Redis per call)");
        rateLimiting.PolicyName.ShouldBe(RateLimitingExtensions.HealthCheckPolicy);
    }
}
