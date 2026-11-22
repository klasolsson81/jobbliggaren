using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// ADR 0188 (#2265) — the three quick-link writes carry <c>MeWritePolicy</c> and an authorization requirement, read
/// from the built endpoint graph in the form of <c>MatchPreferencesRateLimitWiringTests</c>. The junior link's route
/// authorizes itself, because <c>/api/v1/me</c> authorizes per route; the two recent-search routes inherit it from their
/// group. Either way the built endpoint must carry it: <c>MeWritePolicy</c> gives an anonymous caller no limiter.
/// </summary>
[Collection("Api")]
public sealed class QuickLinkWriteRateLimitWiringTests(ApiFactory factory)
{
    [Theory]
    [InlineData("PUT", "/api/v1/me/junior-quick-link")]
    [InlineData("PUT", "/api/v1/me/recent-searches/{id:guid}/quick-link")]
    [InlineData("DELETE", "/api/v1/me/recent-searches/{id:guid}/quick-link")]
    public void The_write_carries_MeWritePolicy_and_requires_authorization(string method, string pattern)
    {
        var endpoint = Endpoint(method, pattern);

        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty($"{method} {pattern} must carry .RequireRateLimiting(...)");
        rateLimiting[^1].PolicyName.ShouldBe(RateLimitingExtensions.MeWritePolicy);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(
            $"{method} {pattern} must require authorization, on the route or its group");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(
            "an [AllowAnonymous] on the route would override its authorization requirement");
    }

    private RouteEndpoint Endpoint(string method, string pattern)
    {
        // Force the server, and with it the endpoint graph, to build before reading the data source.
        _ = factory.CreateClient();

        return factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == pattern
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false))
            .ToList()
            .ShouldHaveSingleItem();
    }
}
