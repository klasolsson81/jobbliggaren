using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0174 — the job sitemap's parts carry their own IP-partitioned policy, no authorization metadata, and the policy
/// sits on that route alone. Read from the built endpoint graph, since a policy name is referenced by string at both
/// ends and a drift is a silent bypass, not a crash.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdListedIdsRateLimitWiringTests(ApiFactory factory)
{
    private const string ListedIdsRoute = "/api/v1/public/job-ads/listed-ids";

    private List<RouteEndpoint> Routes()
    {
        // Force the server, and with it the endpoint graph, to build before reading the data source.
        _ = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void The_listed_ids_are_anonymous_and_carry_their_own_policy()
    {
        var endpoint = Routes()
            .Where(e => e.RoutePattern.RawText == ListedIdsRoute)
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldBeNull(
            "the route is served without a session; it must not inherit an authorization requirement.");
        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.PublicJobAdListedIdsPolicy);
    }

    [Fact]
    public void The_listed_ids_policy_sits_on_that_route_alone()
    {
        Routes()
            .Where(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>() is { Count: > 0 } policies
                        && policies[^1].PolicyName == RateLimitingExtensions.PublicJobAdListedIdsPolicy)
            .Select(e => e.RoutePattern.RawText)
            .ToList()
            .ShouldBe([ListedIdsRoute],
                "a second surface on this bucket would let crawler traffic on one drain the other.");
    }

    [Fact]
    public void PolicyKey_PublicJobAdListedIds_IsStable() =>
        RateLimitingExtensions.PublicJobAdListedIdsPolicy.ShouldBe("public-job-ad-listed-ids");
}
