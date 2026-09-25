using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public class RefreshSessionTests(ApiFactory factory)
{
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string MeEndpoint = "/api/v1/me";

    [Fact]
    public async Task POST_refresh_without_authorization_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync(RefreshEndpoint, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // A Session-profile session never rotates → refresh only slides. Pins the endpoint wiring + the
    // rotated:false contract end-to-end, and that the session still authenticates after. The profile's
    // one producer in src/ is the change-email confirm's fallback in AuthEndpoints, when the current
    // session cannot be read.
    [Fact]
    public async Task POST_refresh_with_session_profile_session_returns_rotated_false_and_keeps_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            factory, lifetime: SessionLifetime.Session, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsync(RefreshEndpoint, content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>(ct);
        body.ShouldNotBeNull();
        body!.Rotated.ShouldBeFalse();
        body.SessionId.ShouldBeNull();

        // Not rotated, only slid → the same id still authenticates.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var me = await client.GetAsync(MeEndpoint, ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record RefreshResponse(bool Rotated, string? SessionId, DateTimeOffset? ExpiresAt);
}
