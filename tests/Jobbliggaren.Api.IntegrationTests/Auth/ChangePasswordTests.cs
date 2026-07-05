using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// End-to-end tests for POST /api/v1/auth/change-password (#678, C5-password + C6 of epik #481) with
/// server-enforced re-auth. The CURRENT password is the re-auth credential (ReauthenticationBehavior
/// verifies it before the handler); on success the endpoint re-issues the current session and logs
/// the user out everywhere (C6). Verifies:
/// <list type="bullet">
/// <item>Auth guard (401 without token)</item>
/// <item>Wrong current password → byte-identical 401 (no oracle) and the password is unchanged</item>
/// <item>Empty current / weak new password → 400 (ValidationBehavior before re-auth)</item>
/// <item>Valid change → 200 with a NEW sessionId, the old id dead, the new id live, and the new
/// password (not the old) logs in</item>
/// <item>C6 logout-everywhere: another device's session is invalidated</item>
/// <item>User.PasswordChanged audit row (AggregateType "User", AggregateId = userId)</item>
/// <item>Persistent lifetime is preserved across the re-issue (persistent:true)</item>
/// </list>
/// Runs against the ApiFactory's real Testcontainers Redis, so the C6 InvalidateAll -> CreateAsync
/// path (COND-B tombstone + fresh session) is exercised end-to-end, not faked.
/// </summary>
[Collection("Api")]
public class ChangePasswordTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // Hardcoded TEST fixture (a new password used by the change-password flow), not a real
    // secret. Tripped the gitleaks generic-api-key heuristic; the inline allow is the durable,
    // rebase/squash-stable fix (a commit-fingerprint in .gitleaksignore breaks on every re-SHA).
    private const string NewPassword = "NyttL0senord123456"; // gitleaks:allow

    // Per-request Authorization so old-vs-new session checks never clobber a shared default header.
    private async Task<HttpResponseMessage> ChangeAsync(string sessionId, string? current, string? updated, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = current, newPassword = updated }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }

    [Fact]
    public async Task POST_change_password_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = AuthTestHelpers.DefaultTestPassword, newPassword = NewPassword },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_change_password_with_wrong_current_password_returns_401_and_does_not_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-wrong-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, "FelLosen123456", NewPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Byte-identical to the shared InvalidCredentials 401 (AuthProblem) — same oracle as /verify.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
        json.GetProperty("detail").GetString().ShouldBe("E-post eller lösenord är felaktigt.");

        // The password did NOT change: the original password still logs in.
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = AuthTestHelpers.DefaultTestPassword }, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_change_password_with_empty_current_returns_400(string? current)
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-emptycur-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, current, NewPassword, ct);

        // ValidationBehavior (NotEmpty on the current password) runs before ReauthenticationBehavior,
        // so empty is 400 (validation), not 401 (re-auth).
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("short")]  // below the 12-char floor
    [InlineData("")]       // empty
    [InlineData(null)]     // missing
    public async Task POST_change_password_with_weak_new_returns_400(string? newPw)
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-weaknew-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, newPw, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_change_password_with_valid_input_reissues_session_and_changes_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-ok-{Guid.NewGuid()}@example.se";
        var oldSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(oldSession, AuthTestHelpers.DefaultTestPassword, NewPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var newSession = json.GetProperty("sessionId").GetString();
        newSession.ShouldNotBeNullOrEmpty();
        newSession.ShouldNotBe(oldSession, "a fresh session id must be minted for the current device");
        // Register defaults to no rememberMe → the Session profile → not a persistent cookie.
        json.GetProperty("persistent").GetBoolean().ShouldBeFalse();

        // C6: the OLD id is dead (logout-everywhere) and the NEW id authenticates.
        (await GetMeAsync(oldSession, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetMeAsync(newSession!, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The password actually changed: the new one logs in, the old one does not.
        (await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = NewPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_change_password_logs_out_other_device_sessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-multi-{Guid.NewGuid()}@example.se";
        var deviceA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var deviceB = await AuthTestHelpers.LoginAndGetSessionIdAsync(_client, email, ct: ct);

        // Both sessions authenticate before the change.
        (await GetMeAsync(deviceB, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ChangeAsync(deviceA, AuthTestHelpers.DefaultTestPassword, NewPassword, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // C6 logout-everywhere: the OTHER device is invalidated.
        (await GetMeAsync(deviceB, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "changing the password must log the user out on every other device");
    }

    [Fact]
    public async Task POST_change_password_writes_User_PasswordChanged_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-audit-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        (await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, NewPassword, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();

        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && e.EventType == "User.PasswordChanged")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.PasswordChanged row per change");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].AggregateId.ShouldBe(user.Id, "the aggregate id is the Identity user id");
    }

    [Fact]
    public async Task POST_change_password_preserves_persistent_lifetime()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"cp-persist-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        // Log in with rememberMe = true → a Persistent session.
        var loginResp = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = AuthTestHelpers.DefaultTestPassword, rememberMe = true },
            ct);
        loginResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var persistentSession = (await loginResp.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("sessionId").GetString();
        persistentSession.ShouldNotBeNullOrEmpty();

        var response = await ChangeAsync(persistentSession!, AuthTestHelpers.DefaultTestPassword, NewPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        // The re-issued session keeps the Persistent profile so the __Host- cookie stays persistent.
        json.GetProperty("persistent").GetBoolean().ShouldBeTrue(
            "a persistent login must remain persistent after a password change");
    }
}
