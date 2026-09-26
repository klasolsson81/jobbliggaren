using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// ADR 0142 part 5a (#1743) — the password routes answer 404 on the Development host, which maps every group.
/// Each body is one the mapped route would have answered with something else: 400, 401 or 202, never 404.
/// <c>/auth/verify</c> was retired in 3a and is a regression pin here. <c>/dev/confirm-email</c> gets an empty
/// address because its own not-found branch also answered 404; an empty one was a 400.
/// </summary>
[Collection("Api")]
public class RetiredPasswordRoutesTests(ApiFactory factory)
{
    private static readonly string[] RetiredRoutes =
    [
        "/api/v1/auth/register",
        "/api/v1/auth/login",
        "/api/v1/auth/change-password",
        "/api/v1/auth/verify-email",
        "/api/v1/auth/resend-confirmation",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/verify",
        "/api/v1/dev/confirm-email",
    ];

    public static TheoryData<string> Routes() => [.. RetiredRoutes];

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_retired_route_answers_404(string route)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        object body = route.EndsWith("/confirm-email", StringComparison.Ordinal)
            ? new { email = "" }
            : new { email = $"retired-{Guid.NewGuid():N}@example.com" };

        var response = await client.PostAsJsonAsync(route, body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void No_retired_route_is_in_the_route_table_under_any_verb()
    {
        // A route that is mapped and happens to answer 404 would pass the theory above; the table cannot.
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .ToList();

        mapped.ShouldNotBeEmpty();
        mapped.Intersect(RetiredRoutes, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
    }

    [Fact]
    public void The_dev_group_maps_exactly_its_three_seams_in_Development()
    {
        // Universally quantified, as the Production route-table test is: a seam added under the group fails here on
        // arrival rather than on someone remembering to add a case.
        var devRoutes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(p => p.StartsWith("/api/v1/dev/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        devRoutes.ShouldBe(["/api/v1/dev/accounts", "/api/v1/dev/login-code", "/api/v1/dev/reset-my-data"]);
    }
}
