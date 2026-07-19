using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #714 — end-to-end tests for POST /api/v1/auth/verify-email (the registration CONFIRM step). PUBLIC
/// (the activation link is opened from the inbox, logged-out): the opaque, SecurityStamp-bound token IS
/// the authorization. The real token is generated in-test via the host UserManager + the same Base64Url
/// transform the production wrapper uses (never parsed out of the recording email sender). Verifies a
/// valid token → 204 + EmailConfirmed + login enabled + NO session issued; a garbage token and an
/// unknown uid → the SAME uniform 400 (no account-existence oracle); and double-click idempotency.
/// Runs against a flag-ON host over the ApiFactory's shared Testcontainers.
/// </summary>
[Collection("Api")]
public class VerifyEmailTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string Password = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register", new { email, password = Password, displayName = "Test User" }, ct);

    private Task<HttpResponseMessage> VerifyAsync(Guid uid, string? token, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { uid, token }, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }, ct);

    private async Task<Guid> GetUserIdAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return user.Id;
    }

    // Generate a REAL confirmation token via the host UserManager, Base64Url-encoded exactly like the
    // production wrapper (UserAccountService.GenerateEmailConfirmationTokenAsync).
    private async Task<string> GenerateUrlSafeTokenAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();
        var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(rawToken));
    }

    // POST verify-email with uid + token as raw STRINGS (never a Guid): an N-format uid must actually
    // reach System.Text.Json's Guid binder — the seam #981 broke. Passing a Guid OBJECT would serialize
    // to the dashed "D" form and hide the bug (which is why the Guid-typed VerifyAsync above never caught it).
    private Task<HttpResponseMessage> PostVerifyRawAsync(string uid, string token, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { uid, token }, ct);

    [Fact]
    public async Task POST_verify_email_with_valid_token_confirms_and_enables_login()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-ok-{Guid.NewGuid()}@example.com";

        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);

        // BEFORE confirming, a login with the correct password is gated (distinct 403).
        (await LoginAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var token = await GenerateUrlSafeTokenAsync(userId, ct);
        var verify = await VerifyAsync(userId, token, ct);

        verify.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await verify.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty("verify issues no session");

        // EmailConfirmed is now set.
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            user.ShouldNotBeNull();
            user.EmailConfirmed.ShouldBeTrue();
        }

        // AFTER confirming, login succeeds (200 + sessionId).
        var login = await LoginAsync(email, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_verify_email_writes_User_EmailConfirmed_audit_with_null_actor()
    {
        // #714 — VerifyEmailCommand is IAuditableCommand (EventType "User.EmailConfirmed", AggregateType
        // "User", ExtractAggregateId → Uid). Parity with the #679 confirm-email-change audit test: a
        // successful confirm writes exactly one audit row, keyed by the TARGET user (AggregateId), and
        // because the confirmer is logged-out the audit ACTOR (UserId) is null. Querying by AggregateId
        // (not UserId) is the load-bearing difference from an authenticated command's audit.
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-audit-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);
        var token = await GenerateUrlSafeTokenAsync(userId, ct);

        (await VerifyAsync(userId, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == userId && e.EventType == "User.EmailConfirmed")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.EmailConfirmed row per confirmed registration");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].UserId.ShouldBeNull("the confirmer is logged-out, so the audit actor is null");
    }

    [Fact]
    public async Task POST_verify_email_with_garbage_token_returns_uniform_400_and_stays_unconfirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-bad-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);

        var response = await VerifyAsync(userId, "not-a-real-token", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailConfirmationToken");

        // Still unconfirmed → login still gated.
        (await LoginAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_verify_email_with_unknown_uid_returns_same_uniform_400_as_bad_token()
    {
        var ct = TestContext.Current.CancellationToken;

        // Oracle A — a garbage token against a KNOWN user.
        var email = $"verify-oracle-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var knownUserId = await GetUserIdAsync(email, ct);
        var badToken = await VerifyAsync(knownUserId, "garbage-token-value", ct);

        // Oracle B — the same request against a NON-EXISTENT user.
        var unknownUid = await VerifyAsync(Guid.NewGuid(), "garbage-token-value", ct);

        // A public endpoint must not distinguish "bad token" from "no such user" (account-existence
        // oracle). Same status, title AND detail (traceId legitimately differs per request).
        badToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknownUid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badJson = await badToken.Content.ReadFromJsonAsync<JsonElement>(ct);
        var unknownJson = await unknownUid.Content.ReadFromJsonAsync<JsonElement>(ct);

        unknownJson.GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailConfirmationToken");
        unknownJson.GetProperty("title").GetString().ShouldBe(badJson.GetProperty("title").GetString());
        unknownJson.GetProperty("detail").GetString().ShouldBe(badJson.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task POST_verify_email_is_idempotent_on_double_click()
    {
        // ConfirmEmailAsync does not rotate the security stamp, so the token stays valid within its
        // lifespan — a double-click (two POSTs of the same token) both return 204 (the safer
        // click-through UX). Contrast the change-email confirm, which is single-use.
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-idem-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);
        var token = await GenerateUrlSafeTokenAsync(userId, ct);

        (await VerifyAsync(userId, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await VerifyAsync(userId, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task POST_verify_email_rejects_cross_user_token_replay()
    {
        // The token is the whole security boundary on a public endpoint: a token minted for user A
        // (bound to A's SecurityStamp) must not confirm user B.
        var ct = TestContext.Current.CancellationToken;
        var emailA = $"verify-xuser-a-{Guid.NewGuid()}@example.com";
        var emailB = $"verify-xuser-b-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(emailA, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await RegisterAsync(emailB, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userIdA = await GetUserIdAsync(emailA, ct);
        var userIdB = await GetUserIdAsync(emailB, ct);

        var tokenForA = await GenerateUrlSafeTokenAsync(userIdA, ct);

        var crossUser = await VerifyAsync(userIdB, tokenForA, ct);
        crossUser.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await crossUser.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.InvalidEmailConfirmationToken");
    }

    [Fact]
    public async Task POST_verify_email_activates_from_the_uid_and_token_in_the_emitted_activation_link()
    {
        // #981 — the REAL activation seam. EmailTemplates.EmailConfirmation renders the exact link the
        // inbox receives (ConsoleEmailSender/ResendEmailSender both render through it). This test takes THAT
        // link, decodes its query the way a browser (URLSearchParams / Next useSearchParams) does, and POSTs
        // the values as strings — so the uid crosses the endpoint's System.Text.Json Guid binder in the form
        // the template emits. That is the one thing the other tests here do NOT do (they POST a Guid object,
        // which STJ writes as "D"), and it is where the bug lived: a compact "N" uid fails STJ's D-only Guid
        // converter -> 400 on every activation. Red before the :N->:D template fix, green after.
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-emitted-link-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);
        var urlSafeToken = await GenerateUrlSafeTokenAsync(userId, ct);

        var rendered = EmailTemplates.EmailConfirmation(
            "https://jobbliggaren.se", new EmailConfirmationEmail(userId, urlSafeToken));
        var link = EmailLinkParsing.ExtractLinkQuery(rendered.PlainTextBody, "/bekrafta-konto");

        // The token must be url-safe at the source (Base64Url) so the query decode below cannot mangle it.
        link["token"].ShouldMatch("^[A-Za-z0-9_-]+$");

        var response = await PostVerifyRawAsync(
            EmailLinkParsing.BrowserDecodeQueryValue(link["uid"]),
            EmailLinkParsing.BrowserDecodeQueryValue(link["token"]),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();
        user.EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Minted_confirmation_token_is_url_safe_base64url_so_it_survives_the_query_round_trip()
    {
        // #981 defect-2 guard. The issue hypothesised a raw-base64 '+' corrupted by the browser query
        // decode — NOT present: the confirmation token has been Base64Url since #714. The activation link
        // embeds the token RAW (no Uri.EscapeDataString), which is correct ONLY while the token stays in the
        // Base64Url alphabet ([A-Za-z0-9_-], no '+'/'/'/'='). Pin the PRODUCTION mint
        // (UserAccountService.GenerateEmailConfirmationTokenAsync, Base64Url-encoded per its own comment):
        // a regression to raw base64 (Convert.ToBase64String) would reintroduce '+', which URLSearchParams
        // turns into a space -> a corrupted token -> 400.
        var ct = TestContext.Current.CancellationToken;
        var email = $"verify-tokenshape-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var userId = await GetUserIdAsync(email, ct);

        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var token = await accounts.GenerateEmailConfirmationTokenAsync(userId, ct);

        token.IsSuccess.ShouldBeTrue();
        token.Value.ShouldMatch("^[A-Za-z0-9_-]+$");
    }
}
