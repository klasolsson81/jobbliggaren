using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #503 (OWASP A07): per-konto kontolåsning vid login. Verifierar att Identitys
/// lockout hedras end-to-end (5 fel -> låst även med rätt lösen — bevisar också att
/// LockoutEnabled=true stämplas vid registrering), att en lyckad login nollar
/// räknaren, och att ett låst konto renderar ett byte-identiskt 401 som fel lösenord
/// (inget lockout-orakel). ApiFactory höjer rate-limits till 10000/60s så dessa
/// flerstegs-flöden inte träffar per-IP-AuthWrite-throttlen.
/// </summary>
[Collection("Api")]
public class LockoutTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // Speglar opts.Lockout.MaxFailedAccessAttempts (DependencyInjection).
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

        // 5 misslyckade försök -> kontot ska nu vara låst.
        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            var attempt = await LoginAsync(email, "WrongPwd!", ct);
            attempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Rätt lösenord ska nu ändå avvisas — lockout kortsluter hash-checken.
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

        // 4 fel (under tröskeln 5) ...
        for (var i = 0; i < MaxFailedAttempts - 1; i++)
            await LoginAsync(email, "WrongPwd!", ct);

        // ... en lyckad login nollar räknaren ...
        (await LoginAsync(email, password, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // ... så ytterligare 4 fel låser INTE (utan reset vore 4+4=8 > 5 -> låst).
        for (var i = 0; i < MaxFailedAttempts - 1; i++)
            await LoginAsync(email, "WrongPwd!", ct);

        (await LoginAsync(email, password, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Locked_account_returns_byte_identical_401_to_wrong_password_no_oracle()
    {
        // #503 G3: ett låst konto får inte vara HTTP-urskiljbart från fel lösenord —
        // annars blir lockout ett enumererings- + DoS-mål-orakel. Speglar
        // LoginTests wrong-vs-unknown-orakeltestet men för lockout-axeln.
        var ct = TestContext.Current.CancellationToken;
        var password = AuthTestHelpers.DefaultTestPassword;

        // Konto A: lås det (5 fel), försök sedan med RÄTT lösen (fortfarande låst).
        var lockedEmail = $"lockout-oracle-a-{Guid.NewGuid()}@example.com";
        await RegisterAsync(lockedEmail, password, ct);
        for (var i = 0; i < MaxFailedAttempts; i++)
            await LoginAsync(lockedEmail, "WrongPwd!", ct);
        var lockedResponse = await LoginAsync(lockedEmail, password, ct);

        // Konto B: enbart fel lösenord (ej låst).
        var wrongPwdEmail = $"lockout-oracle-b-{Guid.NewGuid()}@example.com";
        await RegisterAsync(wrongPwdEmail, password, ct);
        var wrongPwdResponse = await LoginAsync(wrongPwdEmail, "WrongPwd!", ct);

        lockedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPwdResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var lockedJson = await lockedResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var wrongPwdJson = await wrongPwdResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Identisk title OCH detail -> inget orakel mellan "låst" och "fel lösen".
        lockedJson.GetProperty("title").GetString()
            .ShouldBe(wrongPwdJson.GetProperty("title").GetString());
        lockedJson.GetProperty("detail").GetString()
            .ShouldBe(wrongPwdJson.GetProperty("detail").GetString());
        // Hård pin: title läcker aldrig den interna diskriminanten "Auth.AccountLocked".
        lockedJson.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
    }
}
