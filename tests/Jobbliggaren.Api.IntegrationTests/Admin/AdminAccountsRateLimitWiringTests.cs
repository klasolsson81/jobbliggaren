using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1974 (ADR 0151) — both account directory routes carry the admin read bucket, read from the built endpoint
/// graph (the <c>CompaniesRateLimitWiringTests</c> form): a dropped <c>RequireRateLimiting</c> would leave every
/// options test green.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsRateLimitWiringTests(ApiFactory factory)
{
    [Theory]
    [InlineData("POST", "/api/v1/admin/accounts/search")]
    [InlineData("GET", "/api/v1/admin/accounts/{id:guid}")]
    public void The_route_carries_the_admin_read_policy(string method, string pattern)
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == pattern
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull($"{method} {pattern} must carry .RequireRateLimiting(...)");
        rateLimiting.PolicyName.ShouldBe(RateLimitingExtensions.AdminReadPolicy);
    }
}
