using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public class LoginTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_login_with_valid_credentials_returns_session_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"login-{Guid.NewGuid()}@example.com";
        var password = "T3stlosen123456";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password, displayName = "Login User" }, ct);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_login_with_wrong_password_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"wrong-{Guid.NewGuid()}@example.com";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = "T3stlosen123456", displayName = "User" }, ct);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = "WrongPwd!" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_login_wrong_password_and_unknown_account_return_identical_401_no_oracle()
    {
        // #239 Decision 1 (Variant B) security lock — AuthEndpoints keeps Auth.InvalidCredentials→401
        // as an endpoint-local carve-out (401 is the authentication axis, not an ErrorKind), delegating
        // every other failure to the central kind-mapper. The deliberate oracle-avoidance (ADR 0031 /
        // account-deletion runbook): a wrong password and a non-existent account must be HTTP-
        // INDISTINGUISHABLE — identical status AND identical body — so existence is never leaked.
        // This makes the carve-out's security property structural, guarding against a future refactor
        // that drops the Code-branch and silently regresses 401→400.
        var ct = TestContext.Current.CancellationToken;
        var existingEmail = $"oracle-{Guid.NewGuid()}@example.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = existingEmail, password = "T3stlosen123456", displayName = "User" }, ct);

        var wrongPassword = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = existingEmail, password = "WrongPwd!" }, ct);
        var unknownAccount = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = $"nobody-{Guid.NewGuid()}@example.com", password = "T3stlosen123456" }, ct);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongJson = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(ct);
        var unknownJson = await unknownAccount.Content.ReadFromJsonAsync<JsonElement>(ct);
        // Same title (error Code) and detail (message) → no enumeration oracle between the two causes.
        unknownJson.GetProperty("title").GetString()
            .ShouldBe(wrongJson.GetProperty("title").GetString());
        unknownJson.GetProperty("detail").GetString()
            .ShouldBe(wrongJson.GetProperty("detail").GetString());
    }
}
