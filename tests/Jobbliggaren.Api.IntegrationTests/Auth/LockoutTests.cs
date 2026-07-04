using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #503 (OWASP A07): per-account lockout on login. Verifies that Identity's lockout
/// is honored end-to-end (5 failures -> locked even with the correct password — which
/// also proves LockoutEnabled=true is stamped at registration), that a successful login
/// resets the counter, that a locked account renders a byte-identical 401 to a wrong
/// password (no lockout oracle), and that the /verify re-auth path is not an unlocked
/// bypass. ApiFactory raises rate limits to 10000/60s so these multi-step flows do not
/// hit the per-IP AuthWrite throttle.
/// </summary>
[Collection("Api")]
public class LockoutTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // Mirrors opts.Lockout.MaxFailedAccessAttempts (DependencyInjection).
    private const int MaxFailedAttempts = 5;

    private async Task RegisterAsync(string email, string password, CancellationToken ct) =>
        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password, displayName = "Lockout User" }, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, string password, CancellationToken ct) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ct);

    [Fact]
    public async Task Account_locks_after_max_failed_attempts_even_with_correct_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"lockout-{Guid.NewGuid()}@example.com";
        var password = AuthTestHelpers.DefaultTestPassword;
        await RegisterAsync(email, password, ct);

        // 5 failed attempts -> the account must now be locked.
        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            var attempt = await LoginAsync(email, "WrongPwd!", ct);
            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The correct password must now still be rejected — lockout short-circuits the hash check.
        var lockedResponse = await LoginAsync(email, password, ct);
        lockedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Successful_login_resets_failed_attempt_counter()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"lockout-reset-{Guid.NewGuid()}@example.com";
        var password = AuthTestHelpers.DefaultTestPassword;
        await RegisterAsync(email, password, ct);

        // 4 failures (below the threshold of 5) ...
        for (var i = 0; i < MaxFailedAttempts - 1; i++)
            await LoginAsync(email, "WrongPwd!", ct);

        // ... a successful login resets the counter ...
        (await LoginAsync(email, password, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ... so another 4 failures do NOT lock (without the reset it would be 4+4=8 > 5 -> locked).
        for (var i = 0; i < MaxFailedAttempts - 1; i++)
            await LoginAsync(email, "WrongPwd!", ct);

        (await LoginAsync(email, password, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Locked_account_returns_byte_identical_401_to_wrong_password_no_oracle()
    {
        // #503 G3: a locked account must not be HTTP-distinguishable from a wrong password —
        // otherwise lockout becomes an enumeration + DoS-target oracle. Mirrors the LoginTests
        // wrong-vs-unknown oracle test but for the lockout axis.
        var ct = TestContext.Current.CancellationToken;
        var password = AuthTestHelpers.DefaultTestPassword;

        // Account A: lock it (5 failures), then try the CORRECT password (still locked).
        var lockedEmail = $"lockout-oracle-a-{Guid.NewGuid()}@example.com";
        await RegisterAsync(lockedEmail, password, ct);
        for (var i = 0; i < MaxFailedAttempts; i++)
            await LoginAsync(lockedEmail, "WrongPwd!", ct);
        var lockedResponse = await LoginAsync(lockedEmail, password, ct);

        // Account B: a plain wrong password (not locked).
        var wrongPwdEmail = $"lockout-oracle-b-{Guid.NewGuid()}@example.com";
        await RegisterAsync(wrongPwdEmail, password, ct);
        var wrongPwdResponse = await LoginAsync(wrongPwdEmail, "WrongPwd!", ct);

        lockedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPwdResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var lockedJson = await lockedResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var wrongPwdJson = await wrongPwdResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Identical title AND detail -> no oracle between "locked" and "wrong password".
        lockedJson.GetProperty("title").GetString()
            .ShouldBe(wrongPwdJson.GetProperty("title").GetString());
        lockedJson.GetProperty("detail").GetString()
            .ShouldBe(wrongPwdJson.GetProperty("detail").GetString());
        // Hard pin: the title never leaks the internal discriminant "Auth.AccountLocked".
        lockedJson.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Verify_endpoint_also_honors_lockout_no_bypass()
    {
        // #503: /verify (re-auth) goes through the same ValidateCredentialsAsync — it must
        // not be an unlocked brute-force bypass of the login lockout. /verify authenticates
        // via the session (ICurrentUser), so a locked account can still call it and must be
        // rejected on the credential check.
        var ct = TestContext.Current.CancellationToken;
        var email = $"lockout-verify-{Guid.NewGuid()}@example.com";
        var password = AuthTestHelpers.DefaultTestPassword;
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, password, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // 5 failed /verify attempts -> locks the account.
        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            var attempt = await _client.PostAsJsonAsync(
                "/api/v1/auth/verify", new { password = "WrongPwd!" }, ct);
            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The correct password must now still be rejected (locked) — /verify short-circuits like login.
        var locked = await _client.PostAsJsonAsync("/api/v1/auth/verify", new { password }, ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
