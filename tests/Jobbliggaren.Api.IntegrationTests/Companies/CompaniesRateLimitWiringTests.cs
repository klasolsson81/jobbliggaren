using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Companies;

/// <summary>
/// #560 company-search wave — the G4 wiring pin for the <c>/api/v1/companies</c> group, measured
/// from the BUILT endpoint graph (the <c>CompanyWatchCriteriaRateLimitWiringTests</c> pattern —
/// stronger than a key-string pin: a copy-paste that put the wrong policy on the heavy search,
/// or dropped <c>.RequireRateLimiting</c> entirely, silently removes a bulkhead while every
/// options test stays green).
///
/// <para>
/// The two routes deliberately carry DIFFERENT policies: <c>/lookup</c> owns the
/// <c>CompanyLookupPolicy</c> bulkhead because every miss is a potential upstream SCB call once
/// the real adapter activates (ADR 0088 D7); <c>/search</c> reads only the LOCAL register — same
/// cost class as the criterion browse, so it shares the <c>CompanyBrowsePolicy</c> budget
/// (CTO F1). Swapping them would either starve the lookup's upstream budget or hand the heavy
/// register read a policy sized for cache hits.
/// </para>
/// </summary>
[Collection("Api")]
public class CompaniesRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void Every_companies_route_carries_its_intended_rate_limit_policy()
    {
        _ = _factory.CreateClient();

        var routes = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty)
                .Contains("/companies", StringComparison.Ordinal))
            .ToList();

        routes.ShouldNotBeEmpty(
            "the companies endpoint group must be discoverable in the built endpoint graph");

        PolicyFor(routes, "POST", r => r.EndsWith("lookup", StringComparison.Ordinal))
            .ShouldBe(RateLimitingExtensions.CompanyLookupPolicy);
        PolicyFor(routes, "POST", r => r.EndsWith("search", StringComparison.Ordinal))
            .ShouldBe(RateLimitingExtensions.CompanyBrowsePolicy);
    }

    private static string? PolicyFor(
        IEnumerable<RouteEndpoint> routes, string method, Func<string, bool> suffixMatch)
    {
        var endpoint = routes
            .Where(e =>
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false)
                && suffixMatch(e.RoutePattern.RawText ?? string.Empty))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull(
            $"{method} {endpoint.RoutePattern.RawText} must carry .RequireRateLimiting(...)");
        return rateLimiting.PolicyName;
    }
}
