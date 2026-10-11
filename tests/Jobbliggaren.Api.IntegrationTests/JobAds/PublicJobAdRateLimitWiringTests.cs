using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0160 — the anonymous job-ad detail carries its own IP-partitioned policy, no authorization
/// metadata, and the policy sits on that route alone. Read from the built endpoint graph, since a
/// policy name is referenced by string at both ends and a drift is a silent bypass, not a crash.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdRateLimitWiringTests(ApiFactory factory)
{
    private const string PublicDetailRoute = "/api/v1/public/job-ads/{id:guid}";

    private List<RouteEndpoint> Routes()
    {
        // Force the server, and with it the endpoint graph, to build before reading the data source.
        _ = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void The_public_detail_is_anonymous_and_carries_its_own_policy()
    {
        var endpoint = Routes()
            .Where(e => e.RoutePattern.RawText == PublicDetailRoute)
            .ToList()
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldBeNull(
            "the route is served without a session; it must not inherit an authorization requirement.");
        endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()[^1].PolicyName
            .ShouldBe(RateLimitingExtensions.PublicJobAdReadPolicy);
    }

    [Fact]
    public void The_public_job_ad_policy_sits_on_that_route_alone()
    {
        Routes()
            .Where(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>() is { Count: > 0 } policies
                        && policies[^1].PolicyName == RateLimitingExtensions.PublicJobAdReadPolicy)
            .Select(e => e.RoutePattern.RawText)
            .ToList()
            .ShouldBe([PublicDetailRoute],
                "a second surface on this bucket would let crawler traffic on one drain the other.");
    }

    [Fact]
    public void PolicyKey_PublicJobAdRead_IsStable() =>
        RateLimitingExtensions.PublicJobAdReadPolicy.ShouldBe("public-job-ad-read");
}
