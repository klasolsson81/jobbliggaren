using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Auth;

/// <summary>
/// Verifierar att SessionAuthenticationHandler hanterar malformad Authorization-header korrekt.
/// Varje test-case täcker en rad i security-auditor §1.2-tabellen (ADR 0017 Turn 4).
/// </summary>
[Collection("Api")]
public class BearerTokenValidationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // En skyddad endpoint som kräver autentisering
    private const string ProtectedEndpoint = "/api/v1/me";

    [Fact]
    public async Task GET_me_without_authorization_header_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient(); // fresh client — inga default headers

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Bearer", "Bearer utan space/token")]
    [InlineData("Bearer ", "Bearer med space men ingen token")]
    [InlineData("Basic dXNlcjpwYXNz", "Annat schema (Basic)")]
    [InlineData("", "Tom Authorization-header")]
    [InlineData("Bearer foo bar", "Token med space inuti")]
    public async Task GET_me_with_malformed_bearer_returns_401(string headerValue, string scenario)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", headerValue);

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"Scenario: {scenario}");
    }

    [Fact]
    public async Task GET_me_with_token_too_short_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "short");

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_token_too_long_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new string('a', 257));

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_non_base64url_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "token with spaces and !@#");

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_valid_length_but_nonexistent_session_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        // Korrekt format (base64url, 43 tecken) men session existerar inte i Redis
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_me_with_valid_session_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        // Register för att få en giltig session
        var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"bearer-{Guid.NewGuid()}@example.com", password = "T3stlosen123456", displayName = "Bearer Test" },
            ct);
        var json = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var sessionId = json.GetProperty("sessionId").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_me_bearer_scheme_is_case_insensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = $"case-{Guid.NewGuid()}@example.com", password = "T3stlosen123456", displayName = "Case Test" },
            ct);
        var json = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var sessionId = json.GetProperty("sessionId").GetString()!;

        // "bearer" med lowercase — RFC 6750 kräver case-insensitiv matchning
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {sessionId}");

        var response = await client.GetAsync(ProtectedEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
