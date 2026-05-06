using System.Net;
using System.Net.Http.Headers;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Auth;

[Collection("Api")]
public class LogoutTests(ApiFactory factory)
{
    private const string LogoutEndpoint = "/api/v1/auth/logout";
    private const string MeEndpoint = "/api/v1/me";

    [Fact]
    public async Task POST_logout_without_authorization_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync(LogoutEndpoint, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_logout_with_valid_session_returns_204_and_invalidates_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // Logout ska returnera 204
        var logoutResponse = await client.PostAsync(LogoutEndpoint, content: null, ct);
        logoutResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Samma session-id ska nu ge 401
        var meResponse = await client.GetAsync(MeEndpoint, ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_logout_with_already_invalidated_session_returns_401()
    {
        // LogoutCommandHandler.InvalidateAsync är idempotent (ignorerar false-retur vid race).
        // Vid sekventiell double-logout returnerar AuthHandler 401 på andra anropet
        // eftersom sessionen inte finns i Redis — handler nås aldrig.
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var first = await client.PostAsync(LogoutEndpoint, content: null, ct);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await client.PostAsync(LogoutEndpoint, content: null, ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
