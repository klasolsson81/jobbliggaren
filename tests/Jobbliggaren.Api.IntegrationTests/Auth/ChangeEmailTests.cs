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
/// End-to-end tests for POST /api/v1/auth/change-email (#679, C5-email of epik #481) — the REQUEST
/// step, re-auth-gated exactly like /change-password. The CURRENT password is the re-auth credential
/// (verified by ReauthenticationBehavior before the handler); on success the endpoint emails an
/// ownership-confirmation link to the NEW address and returns 202 — WITHOUT changing the email and
/// WITHOUT touching any session (the swap + logout happens only at /confirm-email-change). Verifies:
/// <list type="bullet">
/// <item>Auth guard (401 without token)</item>
/// <item>Wrong current password → byte-identical 401 (no oracle) and no confirmation email is sent</item>
/// <item>Empty current / malformed new email → 400 (ValidationBehavior before re-auth)</item>
/// <item>Taken address → 409 (Auth.EmailTaken) and no confirmation email is sent</item>
/// <item>Valid → 202, a confirmation email recorded to the NEW address, the account UNCHANGED (old
/// address still logs in, new one does not), the caller's session still live, and a
/// User.EmailChangeRequested audit row (AggregateType "User", AggregateId = userId)</item>
/// </list>
/// Runs against the ApiFactory's recording IEmailSender + real Testcontainers Postgres/Redis.
/// </summary>
[Collection("Api")]
public class ChangeEmailTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // Per-request Authorization so session checks never clobber a shared default header.
    private async Task<HttpResponseMessage> ChangeAsync(string sessionId, string? current, string? newEmail, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-email")
        {
            Content = JsonContent.Create(new { currentPassword = current, newEmail }),
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
    public async Task POST_change_email_without_token_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new { currentPassword = AuthTestHelpers.DefaultTestPassword, newEmail = $"ny-{Guid.NewGuid()}@example.se" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_change_email_with_wrong_current_password_returns_401_and_does_not_send()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ce-wrong-{Guid.NewGuid()}@example.se";
        var newEmail = $"ce-wrong-new-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, "FelLosen123456", newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Byte-identical to the shared InvalidCredentials 401 (AuthProblem) — same oracle as /verify and
        // /change-password. A wrong re-auth credential reveals nothing.
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidCredentials");
        json.GetProperty("detail").GetString().ShouldBe("E-post eller lösenord är felaktigt.");

        // A failed re-auth must not mint a token or email the new address.
        _factory.Emails.Sent.ShouldNotContain(e => e.ToEmail == newEmail);

        // The original address still logs in (nothing changed).
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_change_email_with_empty_current_returns_400(string? current)
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ce-emptycur-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, current, $"ce-new-{Guid.NewGuid()}@example.se", ct);

        // ValidationBehavior (NotEmpty on the current password) runs before ReauthenticationBehavior,
        // so empty is 400 (validation), not 401 (re-auth): empty vs wrong = 400 vs 401, revealing nothing.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@no-local.se")]
    [InlineData("")]
    [InlineData(null)]
    public async Task POST_change_email_with_malformed_new_email_returns_400(string? newEmail)
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ce-badnew-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        // Correct current password, so the ONLY failure is the malformed/missing new email.
        var response = await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_change_email_with_taken_address_returns_409_and_does_not_send()
    {
        var ct = TestContext.Current.CancellationToken;
        // Another account already owns the target address.
        var takenEmail = $"ce-taken-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, takenEmail, ct: ct);

        var email = $"ce-taker-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, takenEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.EmailTaken");

        // The 409 pre-check must gate the send: no confirmation email is queued to the taken address.
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == takenEmail && e.Kind == RecordedEmailKind.EmailChangeConfirmation);
    }

    [Fact]
    public async Task POST_change_email_with_valid_input_returns_202_emails_new_address_and_leaves_account_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ce-ok-{Guid.NewGuid()}@example.se";
        var newEmail = $"ce-ok-new-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var response = await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, newEmail, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // A confirmation email was queued to the NEW address, never the old one.
        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == newEmail && e.Kind == RecordedEmailKind.EmailChangeConfirmation);
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.EmailChangeConfirmation);

        // The REQUEST step does NOT change the email: the OLD address still logs in, the new one does not.
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = newEmail, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // No session is touched at the request step — the caller's session is still live.
        (await GetMeAsync(sessionId, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_change_email_writes_User_EmailChangeRequested_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"ce-audit-{Guid.NewGuid()}@example.se";
        var newEmail = $"ce-audit-new-{Guid.NewGuid()}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        (await ChangeAsync(sessionId, AuthTestHelpers.DefaultTestPassword, newEmail, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();

        // The request step actor IS the authenticated user, so UserId (actor) == AggregateId (target).
        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.UserId == user.Id && e.EventType == "User.EmailChangeRequested")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.EmailChangeRequested row per request");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].AggregateId.ShouldBe(user.Id, "the aggregate id is the Identity user id");
    }
}
