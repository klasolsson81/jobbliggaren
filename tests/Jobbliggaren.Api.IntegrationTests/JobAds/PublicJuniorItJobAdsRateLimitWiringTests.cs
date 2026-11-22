using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0186 — the open junior IT list carries its own IP-partitioned policy, no authorization metadata, and the policy
/// sits on that route alone. Read from the built endpoint graph, since a policy name is referenced by string at both
/// ends and a drift is a silent bypass, not a crash.
/// </summary>
[Collection("Api")]
public sealed class PublicJuniorItJobAdsRateLimitWiringTests(ApiFactory factory)
{
    private const string JuniorRoute = "/api/v1/public/job-ads/junior";

    private List<RouteEndpoint> Routes()
    {
        // Force the server, and with it the endpoint graph, to build before reading the data source.
        _ = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void The_junior_list_is_anonymous_and_carries_its_own_policy()
    {
        var endpoint = Routes()
            .Where(e => e.RoutePattern.RawText == JuniorRoute)
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldBeNull(
            "the route is served without a session; it must not inherit an authorization requirement.");
        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.PublicJobAdJuniorItPolicy);
    }

    [Fact]
    public void The_junior_list_policy_sits_on_that_route_alone()
    {
        Routes()
            .Where(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>() is { Count: > 0 } policies
                        && policies[^1].PolicyName == RateLimitingExtensions.PublicJobAdJuniorItPolicy)
            .Select(e => e.RoutePattern.RawText)
            .ToList()
            .ShouldBe([JuniorRoute],
                "a second surface on this bucket would let traffic on one drain the other.");
    }

    [Fact]
    public void PolicyKey_PublicJobAdJuniorIt_IsStable() =>
        RateLimitingExtensions.PublicJobAdJuniorItPolicy.ShouldBe("public-job-ad-junior-it");
}
