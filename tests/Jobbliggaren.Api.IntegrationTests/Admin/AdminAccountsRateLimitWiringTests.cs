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
/// #1974 (ADR 0151) — both account directory routes carry the admin read bucket, read from the built endpoint
/// graph (the <c>CompaniesRateLimitWiringTests</c> form): a dropped <c>RequireRateLimiting</c> would leave every
/// options test green. Since #1975 the address change's two writes carry the admin write bucket and its read the admin
/// read bucket, each with the Admin policy, read as the policy the endpoint EFFECTIVELY carries.
/// </summary>
[Collection("Api")]
public sealed class AdminAccountsRateLimitWiringTests(ApiFactory factory)
{
    private const string EmailChange = "/api/v1/admin/accounts/{id:guid}/email-change";

    [Theory]
    [InlineData("POST", "/api/v1/admin/accounts/search", RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("GET", "/api/v1/admin/accounts/{id:guid}", RateLimitingExtensions.AdminReadPolicy)]
    [InlineData("POST", EmailChange, RateLimitingExtensions.AdminWritePolicy)]
    [InlineData("DELETE", EmailChange, RateLimitingExtensions.AdminWritePolicy)]
    [InlineData("GET", EmailChange, RateLimitingExtensions.AdminReadPolicy)]
    public void The_route_carries_its_admin_policy(string method, string pattern, string policy)
    {
        var endpoint = Endpoint(method, pattern);

        // The policy nearest the endpoint is the one the rate limiter applies.
        var rateLimiting = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeEmpty($"{method} {pattern} must carry .RequireRateLimiting(...)");
        rateLimiting[^1].PolicyName.ShouldBe(policy);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public void The_address_change_routes_require_the_admin_policy(string method)
    {
        Endpoint(method, EmailChange).Metadata.GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(data => data.Policy == AuthorizationPolicies.Admin);
    }

    private RouteEndpoint Endpoint(string method, string pattern)
    {
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
