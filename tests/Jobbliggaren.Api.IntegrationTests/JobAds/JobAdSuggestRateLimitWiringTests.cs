using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1546 — pins the ONE production change `security-auditor`'s Major 1 asked for:
/// <c>/api/v1/job-ads/suggest</c> moved off the shared <c>SuggestPolicy</c> onto a dedicated
/// <c>JobAdSuggestPolicy</c>, and <c>/saved-searches/derive</c> did NOT move with it.
///
/// <para>
/// <b>Why the wiring needs its own fact.</b> ASP.NET resolves a named policy at REQUEST time, so a
/// dropped <c>AddPolicy</c> or a revert of one <c>.RequireRateLimiting(...)</c> argument leaves this
/// whole repository green: every suggest test constructs the handler directly, and the frontend tests
/// stub <c>fetch</c>. Nothing in the suite makes an HTTP request to this surface. The
/// <c>CompanyWatchCriteriaRateLimitWiringTests</c> precedent reads the built endpoint graph instead,
/// deterministically and with no burst, and that is what this mirrors.
/// </para>
///
/// <para>
/// <b>The pair is the point, not either half.</b> The reason for the split is that the employer
/// branch runs a <c>%contains%</c> + <c>GROUP BY</c> over <c>job_ads</c> while saved-searches carries
/// none of that weight. A change that tightened BOTH surfaces would satisfy a one-sided assertion and
/// would be exactly the collateral the split exists to avoid, so both are asserted here.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdSuggestRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void JobAdSuggest_CarriesItsOwnPolicy_AndSavedSearchSuggestDoesNot()
    {
        // Force the server (and thus the endpoint graph) to build before reading the data source.
        _ = _factory.CreateClient();

        var routes = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        routes.ShouldNotBeEmpty("the endpoint graph must be discoverable");

        PolicyFor(routes, "GET", "/api/v1/job-ads/suggest")
            .ShouldBe(
                RateLimitingExtensions.JobAdSuggestPolicy,
                "this surface runs the employer branch's %contains% + GROUP BY over job_ads once per "
                + "keystroke. Leaving it on the shared SuggestPolicy puts that scan on a lighter "
                + "budget than /job-ads/employers sits on for the same query form.");

        PolicyFor(routes, "GET", "/api/v1/saved-searches/derive")
            .ShouldBe(
                RateLimitingExtensions.SuggestPolicy,
                "the shared typeahead policy is deliberately UNCHANGED for the saved-search derive "
                + "step, which is typeahead-shaped but carries none of this weight. Tightening "
                + "it alongside the split would be collateral, not calibration.");
    }

    /// <summary>
    /// The key strings, byte for byte. They are referenced by NAME at both ends — the policy
    /// registration and the endpoint's attribute — so drift between them is a silent bypass rather
    /// than a startup failure. Mirrors <c>RateLimitingOptionsTests.PolicyKeys_*_AreStable</c>.
    /// </summary>
    [Fact]
    public void PolicyKey_JobAdSuggest_IsStable()
    {
        RateLimitingExtensions.JobAdSuggestPolicy.ShouldBe("job-ad-suggest");

        RateLimitingExtensions.JobAdSuggestPolicy.ShouldNotBe(
            RateLimitingExtensions.SuggestPolicy,
            "a shared key would silently re-merge the two budgets the split exists to separate.");
    }

    private static string? PolicyFor(
        IEnumerable<RouteEndpoint> routes, string method, string rawRoute)
    {
        var endpoint = routes
            .Where(e =>
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false)
                && string.Equals(
                    e.RoutePattern.RawText?.TrimStart('/'),
                    rawRoute.TrimStart('/'),
                    StringComparison.Ordinal))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull(
            $"{method} {rawRoute} must carry .RequireRateLimiting(...)");
        return rateLimiting.PolicyName;
    }
}
