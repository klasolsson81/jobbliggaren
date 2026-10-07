using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #1979 — every feedback route carries its bucket, read from the built endpoint graph (the
/// <c>AdminAccountsRateLimitWiringTests</c> form): a dropped <c>RequireRateLimiting</c> would leave every options test
/// green. The six admin routes also carry the Admin policy.
/// </summary>
[Collection("Api")]
public sealed class FeedbackRateLimitWiringTests(ApiFactory factory)
{
    private const string Admin = "/api/v1/admin/feedback";

    [Theory]
    [InlineData("POST", "/api/v1/me/feedback", RateLimitingExtensions.FeedbackSubmitPolicy)]
    [InlineData("GET", "/api/v1/me/feedback/prompt-state", RateLimitingExtensions.FeedbackPromptStatePolicy)]
    [InlineData("GET", Admin, RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("GET", Admin + "/summary", RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("GET", Admin + "/availability", RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("GET", Admin + "/{id:guid}", RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("POST", Admin + "/{id:guid}/status", RateLimitingExtensions.AdminWritePolicy)]
    [InlineData("POST", Admin + "/{id:guid}/notification/requeue", RateLimitingExtensions.AdminWritePolicy)]
    public void The_route_carries_its_bucket(string method, string pattern, string policy)
    {
        // The policy nearest the endpoint is the one the rate limiter applies.
        var rateLimiting = Endpoint(method, pattern).Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty($"{method} {pattern} must carry .RequireRateLimiting(...)");
        rateLimiting[^1].PolicyName.ShouldBe(policy);
    }

    [Theory]
    [InlineData("GET", Admin)]
    [InlineData("GET", Admin + "/summary")]
    [InlineData("GET", Admin + "/availability")]
    [InlineData("GET", Admin + "/{id:guid}")]
    [InlineData("POST", Admin + "/{id:guid}/status")]
    [InlineData("POST", Admin + "/{id:guid}/notification/requeue")]
    public void The_admin_route_requires_the_admin_policy(string method, string pattern) =>
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
