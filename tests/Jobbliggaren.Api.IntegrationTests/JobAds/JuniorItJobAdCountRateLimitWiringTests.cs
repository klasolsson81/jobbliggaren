using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0188 (#2265) — the junior quick link's count carries its own per-user policy and an authorization requirement,
/// and the policy sits on that route alone. Read from the built endpoint graph, the form of
/// <c>PublicJuniorItJobAdsRateLimitWiringTests</c>: a policy name is referenced by string at both ends, and the policy
/// gives an anonymous caller no limiter, so a dropped requirement at either end is a silent bypass, not a crash.
/// </summary>
[Collection("Api")]
public sealed class JuniorItJobAdCountRateLimitWiringTests(ApiFactory factory)
{
    private const string CountRoute = "/api/v1/job-ads/junior-it-count";

    private List<RouteEndpoint> Routes()
    {
        // Force the server, and with it the endpoint graph, to build before reading the data source.
        _ = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void The_count_requires_a_session_and_carries_its_own_policy()
    {
        var endpoint = Routes()
            .Where(e => e.RoutePattern.RawText == CountRoute
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") ?? false))
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(
            "the per-user policy gives an anonymous caller no limiter; only the 401 in front of it makes that safe.");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(
            "an [AllowAnonymous] on the route would override its authorization requirement");
        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.JobAdJuniorItCountPolicy);
    }

    [Fact]
    public void The_count_policy_sits_on_that_route_alone()
    {
        Routes()
            .Where(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>() is { Count: > 0 } policies
                        && policies[^1].PolicyName == RateLimitingExtensions.JobAdJuniorItCountPolicy)
            .Select(e => e.RoutePattern.RawText)
            .ToList()
            .ShouldBe([CountRoute], "a second surface on this bucket would let traffic on one drain the other.");
    }

    [Fact]
    public void PolicyKey_JobAdJuniorItCount_IsStable()
    {
        RateLimitingExtensions.JobAdJuniorItCountPolicy.ShouldBe("job-ad-junior-it-count");

        RateLimitingExtensions.JobAdJuniorItCountPolicy.ShouldNotBe(
            RateLimitingExtensions.PublicJobAdJuniorItPolicy,
            "the open list's bucket is per client IP and anonymous; sharing it would let either drain the other.");
    }
}
