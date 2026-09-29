using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// #1918 — the per-part match-preferences write carries <c>MeWritePolicy</c> and an
/// authorization requirement, read from the built endpoint graph rather than the source, in the form of
/// <c>CompanyWatchCriteriaRateLimitWiringTests</c>. This route only: the rest of <c>/me</c> belongs to
/// #1921.
/// </summary>
[Collection("Api")]
public class MatchPreferencesRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void PATCH_match_preferences_carries_MeWritePolicy_and_requires_authorization()
    {
        // Force the server (and thus the endpoint graph) to build before we read the data source.
        _ = _factory.CreateClient();

        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PATCH") ?? false)
                && string.Equals(
                    (e.RoutePattern.RawText ?? string.Empty).TrimEnd('/'),
                    "/api/v1/me/match-preferences",
                    StringComparison.Ordinal))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull("PATCH /api/v1/me/match-preferences must carry .RequireRateLimiting(...)");
        rateLimiting.PolicyName.ShouldBe(RateLimitingExtensions.MeWritePolicy);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(
            "PATCH /api/v1/me/match-preferences must carry .RequireAuthorization()");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(
            "an [AllowAnonymous] on the route would override its authorization requirement");
    }
}
