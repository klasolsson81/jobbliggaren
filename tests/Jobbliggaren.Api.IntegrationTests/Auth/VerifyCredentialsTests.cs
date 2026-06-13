using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// TD-28 re-autentisering före destruktiv operation. Validerar lösenord
/// utan att skapa eller mutera session.
/// </summary>
[Collection("Api")]
public class VerifyCredentialsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_verify_with_valid_password_returns_204()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-{Guid.NewGuid()}@example.com";
        var password = "T3stlosen123456";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email, password, ct: ct);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/verify",
            new { password },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task POST_verify_with_wrong_password_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-wrong-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email, "T3stlosen123456", ct: ct);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/verify",
            new { password = "WrongPwd!" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_verify_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/verify",
            new { password = "anything" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_verify_does_not_create_or_change_session()
    {
        // Säkerhetsinvariant: verify ändrar INTE sessioner — den ursprungliga
        // session-id:n ska fortfarande fungera efter verify.
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-noop-{Guid.NewGuid()}@example.com";
        var password = "T3stlosen123456";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email, password, ct: ct);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var verifyResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/verify",
            new { password },
            ct);
        verifyResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Originalsessionen fungerar fortfarande för efterföljande request.
        var meResponse = await _client.GetAsync("/api/v1/me/", ct);
        meResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
