using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

/// <summary>
/// #2353 — the saved CV's language write carries <c>MeWritePolicy</c> and an authorization
/// requirement, read from the built endpoint graph in the form of
/// <c>MatchPreferencesRateLimitWiringTests</c>. The review page's language choice calls it, and each
/// call reconciles the finding ledger by running the review.
/// </summary>
[Collection("Api")]
public class ResumeLanguageRateLimitWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void PUT_resume_language_carries_MeWritePolicy_and_requires_authorization()
    {
        // Force the server (and thus the endpoint graph) to build before we read the data source.
        _ = _factory.CreateClient();

        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PUT") ?? false)
                && string.Equals(
                    (e.RoutePattern.RawText ?? string.Empty).TrimEnd('/'),
                    "/api/v1/resumes/{id:guid}/language",
                    StringComparison.Ordinal))
            .ToList()
            .ShouldHaveSingleItem();

        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.ShouldNotBeNull("PUT /api/v1/resumes/{id}/language must carry .RequireRateLimiting(...)");
        rateLimiting.PolicyName.ShouldBe(RateLimitingExtensions.MeWritePolicy);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty(
            "PUT /api/v1/resumes/{id}/language must carry .RequireAuthorization()");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(
            "an [AllowAnonymous] on the route would override its authorization requirement");
    }
}
