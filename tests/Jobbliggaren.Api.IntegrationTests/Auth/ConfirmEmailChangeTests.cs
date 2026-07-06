using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
/// End-to-end tests for POST /api/v1/auth/confirm-email-change (#679) — the CONFIRM step. The endpoint
/// is PUBLIC (the link is opened from the new inbox, possibly logged-out): the opaque, single-use,
/// SecurityStamp-bound token IS the authorization. The real token is generated in-test by resolving
/// the host's <see cref="UserManager{ApplicationUser}"/> and applying the same Base64Url transform the
/// production wrapper does — it is NEVER parsed out of the recording email sender. Verifies:
/// <list type="bullet">
/// <item>A valid token with NO auth cookie → 204; Email + EmailConfirmed set; UserName kept in lockstep
/// (CTO risk 1); ALL prior sessions invalidated (C6); login with the NEW email + unchanged password
/// works; an old-address notice recorded; a User.EmailChanged audit row (actor null, AggregateId = uid)</item>
/// <item>A garbage token → uniform 400 and the email is unchanged</item>
/// <item>A non-existent uid → the SAME uniform 400 as a bad token (oracle parity: no account-existence
/// oracle on a public endpoint)</item>
/// <item>TOCTOU — a second account owns the new address at confirm time → uniform 400 (ChangeEmailAsync
/// re-runs RequireUniqueEmail, the authoritative backstop)</item>
/// </list>
/// Runs against the ApiFactory's recording IEmailSender + real Testcontainers Postgres/Redis, so the
/// C6 InvalidateAll path is exercised end-to-end.
/// </summary>
[Collection("Api")]
public class ConfirmEmailChangeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // PUBLIC endpoint — no Authorization header (the token is the authorization).
    private async Task<HttpResponseMessage> ConfirmAsync(Guid uid, string? email, string? token, CancellationToken ct)
        => await _client.PostAsJsonAsync(
            "/api/v1/auth/confirm-email-change", new { uid, email, token }, ct);

    private async Task<HttpResponseMessage> GetMeAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }

    private async Task<Guid> GetUserIdAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return user.Id;
    }

    // Generate a REAL change-email token via the host's UserManager, applying the exact transform of the
    // production wrapper (UserAccountService.GenerateChangeEmailTokenAsync): the opaque DataProtector
    // token, Base64Url-encoded so it survives the link -> query -> POST round-trip.
    private async Task<string> GenerateUrlSafeTokenAsync(Guid userId, string newEmail, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();
        var rawToken = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(rawToken));
    }

    [Fact]
    public async Task POST_confirm_email_change_with_valid_token_swaps_email_and_logs_out_everywhere()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = $"cec-ok-old-{Guid.NewGuid()}@example.se";
        var newEmail = $"cec-ok-new-{Guid.NewGuid()}@example.se";

        // Two devices so we can prove ALL sessions are invalidated (C6), not just the confirmer's.
        var deviceA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, oldEmail, ct: ct);
        var deviceB = await AuthTestHelpers.LoginAndGetSessionIdAsync(_client, oldEmail, ct: ct);

        var userId = await GetUserIdAsync(oldEmail, ct);
        var token = await GenerateUrlSafeTokenAsync(userId, newEmail, ct);

        // No auth header — the endpoint is public and token-gated.
        var response = await ConfirmAsync(userId, newEmail, token, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Email + EmailConfirmed set, and UserName kept in lockstep with Email (CTO risk 1: login
        // resolves by email, so a stale UserName would strand the account).
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            user.ShouldNotBeNull();
            user.Email.ShouldBe(newEmail);
            user.EmailConfirmed.ShouldBeTrue();
            user.UserName.ShouldBe(newEmail);
        }

        // C6 — every prior session is invalidated (the recovery vector changed).
        (await GetMeAsync(deviceA, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetMeAsync(deviceB, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Login with the NEW email + the unchanged password succeeds; the OLD email no longer resolves.
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = newEmail, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = oldEmail, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A security notice was queued to the OLD address, never the new one.
        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == oldEmail && e.Kind == RecordedEmailKind.EmailChangedNotification);
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == newEmail && e.Kind == RecordedEmailKind.EmailChangedNotification);
    }

    [Fact]
    public async Task POST_confirm_email_change_writes_User_EmailChanged_audit_with_null_actor()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = $"cec-audit-old-{Guid.NewGuid()}@example.se";
        var newEmail = $"cec-audit-new-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, oldEmail, ct: ct);

        var userId = await GetUserIdAsync(oldEmail, ct);
        var token = await GenerateUrlSafeTokenAsync(userId, newEmail, ct);

        (await ConfirmAsync(userId, newEmail, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The confirmer is logged-out → the audit ACTOR (UserId) is null; the TARGET is the AggregateId.
        // Querying by AggregateId (not UserId) is the load-bearing difference from the request-step audit.
        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == userId && e.EventType == "User.EmailChanged")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.EmailChanged row per confirmed change");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].UserId.ShouldBeNull("the confirmer is logged-out, so the audit actor is null");
    }

    [Fact]
    public async Task POST_confirm_email_change_with_garbage_token_returns_uniform_400_and_does_not_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = $"cec-badtok-{Guid.NewGuid()}@example.se";
        var newEmail = $"cec-badtok-new-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, oldEmail, ct: ct);
        var userId = await GetUserIdAsync(oldEmail, ct);

        var response = await ConfirmAsync(userId, newEmail, "not-a-real-token", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailChangeToken");

        // The email did NOT change — the old address still logs in.
        (await _client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = oldEmail, password = AuthTestHelpers.DefaultTestPassword }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_confirm_email_change_with_unknown_uid_returns_same_uniform_400_as_bad_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var newEmail = $"cec-oracle-new-{Guid.NewGuid()}@example.se";

        // Oracle A — a garbage token against a KNOWN user.
        var knownEmail = $"cec-oracle-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, knownEmail, ct: ct);
        var knownUserId = await GetUserIdAsync(knownEmail, ct);
        var badTokenResponse = await ConfirmAsync(knownUserId, newEmail, "garbage-token-value", ct);

        // Oracle B — the same well-formed request against a NON-EXISTENT user.
        var unknownUidResponse = await ConfirmAsync(Guid.NewGuid(), newEmail, "garbage-token-value", ct);

        // A public endpoint must not distinguish "bad token" from "no such user", or it becomes an
        // account-existence oracle. Same status, same title, same detail (the traceId legitimately
        // differs per request, so we compare the semantic surface, not the whole body).
        badTokenResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknownUidResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badJson = await badTokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var unknownJson = await unknownUidResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        unknownJson.GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailChangeToken");
        unknownJson.GetProperty("title").GetString().ShouldBe(badJson.GetProperty("title").GetString());
        unknownJson.GetProperty("detail").GetString().ShouldBe(badJson.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task POST_confirm_email_change_when_address_taken_at_confirm_returns_uniform_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldEmail = $"cec-toctou-old-{Guid.NewGuid()}@example.se";
        var takenEmail = $"cec-toctou-taken-{Guid.NewGuid()}@example.se";

        // User A requests a change to takenEmail (mint a real token bound to A + takenEmail) ...
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, oldEmail, ct: ct);
        var userIdA = await GetUserIdAsync(oldEmail, ct);
        var token = await GenerateUrlSafeTokenAsync(userIdA, takenEmail, ct);

        // ... but before A confirms, a SECOND account claims takenEmail (TOCTOU).
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, takenEmail, ct: ct);

        var response = await ConfirmAsync(userIdA, takenEmail, token, ct);

        // ChangeEmailAsync re-runs RequireUniqueEmail — the authoritative uniqueness backstop — and the
        // rejection is the SAME uniform 400 (no oracle that the address is taken vs the token is bad).
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailChangeToken");

        // A's address is unchanged.
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userA = await userManager.FindByIdAsync(userIdA.ToString());
        userA.ShouldNotBeNull();
        userA.Email.ShouldBe(oldEmail);
    }
}
