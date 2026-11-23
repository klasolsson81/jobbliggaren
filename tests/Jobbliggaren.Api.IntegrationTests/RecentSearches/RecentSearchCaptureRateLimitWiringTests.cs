using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// #2285 — the capture POST carries its own bucket and an authorization requirement, read from the built endpoint graph
/// in the form of <c>QuickLinkWriteRateLimitWiringTests</c>. The policy gives an anonymous caller no limiter, so the
/// requirement is what keeps the route closed to one.
/// </summary>
[Collection("Api")]
public sealed class RecentSearchCaptureRateLimitWiringTests(ApiFactory factory)
{
    [Fact]
    public void The_capture_post_carries_its_own_policy_and_requires_authorization()
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == "/api/v1/me/recent-searches/"
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") ?? false))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty("the capture POST must carry .RequireRateLimiting(...)");
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.RecentSearchCapturePolicy);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(
            "the capture POST must require authorization, on the route or its group");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(
            "an [AllowAnonymous] on the route would override its authorization requirement");
    }
}
