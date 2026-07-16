using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 PR-3 — pins that every criteria route actually CARRIES its intended rate-limit policy, read
/// from the built endpoint graph (not from the source). The house's <c>RateLimitingOptionsTests</c>
/// pin the policy NUMBERS and the constant STRINGS; the burst-factory tests prove a handful of
/// policies THROTTLE. Neither proves that <em>this</em> route wired <em>this</em> policy — yet a
/// copy-paste that put <c>MeWritePolicy</c> on the heavy browse, or that dropped
/// <c>.RequireRateLimiting</c> from <c>preview-count</c>, silently removes a bulkhead the whole
/// policy file exists to provide. This asserts the wiring by measurement, deterministically, with no
/// burst and no extra container (it reuses the shared Api host).
/// </summary>
[Collection("Api")]
public class CompanyWatchCriteriaRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void Every_criteria_route_carries_its_intended_rate_limit_policy()
    {
        // Force the server (and thus the endpoint graph) to build before we read the data source.
        _ = _factory.CreateClient();

        var routes = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty)
                .Contains("company-watch-criteria", StringComparison.Ordinal))
            .ToList();

        routes.ShouldNotBeEmpty(
            "the criteria endpoint group must be discoverable in the built endpoint graph");

        // GET  base           -> a light per-user read (MeListRead, NOT the browse policy).
        PolicyFor(routes, "GET", IsBase).ShouldBe(RateLimitingExtensions.MeListReadPolicy);
        // POST base           -> create (MeWrite).
        PolicyFor(routes, "POST", IsBase).ShouldBe(RateLimitingExtensions.MeWritePolicy);
        // GET  /reference      -> the static taxonomy tree (TaxonomyRead mold).
        PolicyFor(routes, "GET", r => r.EndsWith("reference", StringComparison.Ordinal))
            .ShouldBe(RateLimitingExtensions.TaxonomyReadPolicy);
        // GET  /{id}/companies -> the heaviest read in the house (its OWN CompanyBrowse bucket).
        PolicyFor(routes, "GET", r => r.EndsWith("companies", StringComparison.Ordinal))
            .ShouldBe(RateLimitingExtensions.CompanyBrowsePolicy);
        // POST /preview-count  -> the picker's live magnitude preview (its OWN CriterionCountPreview).
        PolicyFor(routes, "POST", r => r.EndsWith("preview-count", StringComparison.Ordinal))
            .ShouldBe(RateLimitingExtensions.CriterionCountPreviewPolicy);
        // PATCH/DELETE /{id}   -> mutations (MeWrite).
        PolicyFor(routes, "PATCH", IsIdRoute).ShouldBe(RateLimitingExtensions.MeWritePolicy);
        PolicyFor(routes, "DELETE", IsIdRoute).ShouldBe(RateLimitingExtensions.MeWritePolicy);
    }

    // The group root ".../company-watch-criteria" (both the GET list and the POST create map "/").
    private static bool IsBase(string raw) =>
        raw.TrimEnd('/').EndsWith("company-watch-criteria", StringComparison.Ordinal);

    // The "/{id}" mutation routes — end at the id token, and are NOT the "/{id}/companies" browse.
    private static bool IsIdRoute(string raw) =>
        !raw.EndsWith("companies", StringComparison.Ordinal)
        && raw.TrimEnd('/').EndsWith('}');

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
