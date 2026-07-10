using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #714 — the email-confirmation-first LOGIN gate. An unconfirmed account whose password is CORRECT
/// gets a distinct, actionable 403 (Auth.EmailNotConfirmed); a wrong password / unknown account still
/// gets the byte-identical 401 (Auth.InvalidCredentials). Because the 403 is reachable ONLY after a
/// valid password, it is not an account-enumeration oracle — pinned here. Runs against a flag-ON host.
/// </summary>
[Collection("Api")]
public class LoginEmailConfirmationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string Password = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = Password, displayName = "Test User" }, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, string password, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ct);

    [Fact]
    public async Task POST_login_unconfirmed_correct_password_returns_403_email_not_confirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"logingate-403-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var response = await LoginAsync(email, Password, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe(AuthErrorCodes.EmailNotConfirmed);
        json.GetProperty("detail").GetString().ShouldBe(AuthErrorCodes.EmailNotConfirmedMessage);
    }

    [Fact]
    public async Task POST_login_unconfirmed_wrong_password_returns_401_not_403()
    {
        // The gate is reached ONLY after a correct password. A wrong password on an unconfirmed account
        // takes the ordinary InvalidCredentials 401 path — so the 403 never leaks account existence to
        // someone who does not already hold the password.
        var ct = TestContext.Current.CancellationToken;
        var email = $"logingate-wrongpw-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var response = await LoginAsync(email, "WrongPassword-999999", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task POST_login_unknown_account_and_unconfirmed_wrong_password_are_identical_401()
    {
        // Enumeration surface stays uniform: an unknown account and a known-but-unconfirmed account with
        // a wrong password produce the byte-identical 401 (status + title + detail).
        var ct = TestContext.Current.CancellationToken;
        var knownEmail = $"logingate-oracle-known-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(knownEmail, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var unknownResponse = await LoginAsync($"logingate-oracle-unknown-{Guid.NewGuid()}@example.com", Password, ct);
        var knownWrongPw = await LoginAsync(knownEmail, "WrongPassword-999999", ct);

        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        knownWrongPw.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var unknownJson = await unknownResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var knownJson = await knownWrongPw.Content.ReadFromJsonAsync<JsonElement>(ct);
        unknownJson.GetProperty("title").GetString().ShouldBe(knownJson.GetProperty("title").GetString());
        unknownJson.GetProperty("detail").GetString().ShouldBe(knownJson.GetProperty("detail").GetString());
    }
}
