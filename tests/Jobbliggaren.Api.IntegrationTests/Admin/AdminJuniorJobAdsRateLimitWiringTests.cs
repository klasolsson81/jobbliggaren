using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #2264 (ADR 0187) — both admin junior routes carry their bucket and the Admin policy, read from the built endpoint
/// graph (the <c>FeedbackRateLimitWiringTests</c> form): a dropped <c>RequireRateLimiting</c> would leave every options
/// test green.
/// </summary>
[Collection("Api")]
public sealed class AdminJuniorJobAdsRateLimitWiringTests(ApiFactory factory)
{
    private const string Queue = "/api/v1/admin/junior-job-ads";

    [Theory]
    [InlineData("GET", Queue, RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("POST", Queue + "/{id:guid}/decision", RateLimitingExtensions.AdminWritePolicy)]
    public void The_route_carries_its_bucket(string method, string pattern, string policy)
    {
        // The policy nearest the endpoint is the one the rate limiter applies.
        var rateLimiting = Endpoint(method, pattern).Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty($"{method} {pattern} must carry .RequireRateLimiting(...)");
        rateLimiting[^1].PolicyName.ShouldBe(policy);
    }

    [Theory]
    [InlineData("GET", Queue)]
    [InlineData("POST", Queue + "/{id:guid}/decision")]
    public void The_route_requires_the_admin_policy(string method, string pattern) =>
        Endpoint(method, pattern).Metadata.GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);

    private RouteEndpoint Endpoint(string method, string pattern)
    {
        _ = factory.CreateClient();

        // A group's "/" route ends in a slash; the pattern is compared without it.
        return factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimEnd('/') == pattern
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false))
            .ToList()
            .ShouldHaveSingleItem();
    }
}
