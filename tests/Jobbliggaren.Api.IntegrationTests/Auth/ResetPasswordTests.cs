using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1171 — password reset, APPLY step (POST /api/v1/auth/reset-password). PUBLIC and token-gated: the
/// link is opened from the account's own inbox, logged out by definition, so the opaque single-use token
/// IS the authorization. Every test drives the REAL link — minted through
/// <see cref="IUserAccountService.TryPreparePasswordResetAsync"/> and rendered by
/// <c>EmailTemplates.PasswordReset</c> — then decodes its query the way a browser
/// (<c>URLSearchParams</c> / Next's <c>useSearchParams</c>) does and POSTs the values as STRINGS, so the
/// uid crosses the endpoint's System.Text.Json Guid binder in the form the template actually emits. A
/// test that POSTs a <see cref="Guid"/> object never crosses that seam (STJ writes the dashed "D" form),
/// which is exactly why the pre-existing activation tests stayed green while every real click 400'd
/// (#981). Verifies:
/// <list type="bullet">
/// <item>The emitted link works end to end: 204, the OLD password 401s at login and the NEW one succeeds</item>
/// <item>Single-use: the same link a second time is 400 (the security stamp rotates only on success)</item>
/// <item>A bad token, a malformed token and an unknown uid all collapse to ONE uniform 400
/// (<c>Auth.InvalidPasswordResetToken</c>) — telling them apart would make a public endpoint an
/// account-existence oracle</item>
/// <item>A breached password is 400 <c>Auth.PwnedPassword</c> AND THE SAME LINK STILL WORKS afterwards:
/// Identity verifies the token BEFORE running the password validators, so a rejected password must not
/// burn the token and strand a user whose only remaining credential is that link</item>
/// <item>A too-short password is rejected as a PASSWORD failure, not as a bad link</item>
/// <item>A successful reset tears down every OTHER session (real Testcontainers Redis) and issues NO
/// session of its own — recovery must not become login for whoever opened the link</item>
/// <item>A successful reset writes exactly one <c>User.PasswordReset</c> audit row (AggregateType
/// "User", AggregateId = the account), with a null actor because the resetter is anonymous</item>
/// <item>A successful reset sends the PasswordChangedNotice — the one moment a real owner can notice a
/// reset they did not perform (OWASP ASVS V2.5 / NIST SP 800-63B)</item>
/// </list>
/// Every test runs on the BASE <c>factory.CreateClient()</c>; a dedicated host would be a fourth
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> and would breach
/// EF's process-global <c>ManyServiceProvidersCreatedWarning</c> ceiling (#1190). HIBP verdicts are
/// steered per password through the ApiFactory stub, so passwords are unique per test.
/// </summary>
[Collection("Api")]
public class ResetPasswordTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private const string BaseUrl = "https://jobbliggaren.se";

    // Hardcoded TEST fixture passwords, not real secrets. Unique per test because the breach-check stub
    // is a dictionary keyed by password and shared across the whole "Api" collection.
    private const string EndToEndPassword = "AterstalltL0sen1234";   // gitleaks:allow
    private const string SingleUsePassword = "EngangsL0sen123456";   // gitleaks:allow
    private const string RejectedTokenPassword = "TokenTestL0sen1234"; // gitleaks:allow
    private const string BreachedPassword = "BreachedResetL0sen1";   // gitleaks:allow
    private const string CleanRetryPassword = "RentL0senEfterPwn12"; // gitleaks:allow
    private const string TooShortPassword = "kort";                 // gitleaks:allow
    private const string SessionPassword = "SessionL0sen123456";     // gitleaks:allow
    private const string AuditPassword = "AuditL0sen1234567";        // gitleaks:allow
    private const string NoticePassword = "NoticeL0sen123456";       // gitleaks:allow

    /// <summary>The (uid, token) pair a real inbox receives, already browser-decoded.</summary>
    private sealed record ResetLink(Guid UserId, string Uid, string Token);

    // Mint through the PRODUCTION eligibility+mint port and render through the PRODUCTION template, then
    // read the link back out of the rendered body. Nothing about the pair is hand-built: a template that
    // stopped emitting the dashed uid, or a mint that regressed out of the Base64Url alphabet, breaks
    // every success test in this class instead of leaving them green against a link nobody sends.
    private async Task<ResetLink> EmittedResetLinkAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var delivery = await accounts.TryPreparePasswordResetAsync(email, ct);
        delivery.ShouldNotBeNull();

        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(delivery.UserId, delivery.UrlSafeToken));
        var link = EmailLinkParsing.ExtractLinkQuery(rendered.PlainTextBody, "/aterstall-losenord");

        // The token must be url-safe AT THE SOURCE (Base64Url), because the link embeds it raw. A
        // regression to plain base64 would reintroduce '+', which URLSearchParams turns into a space —
        // a corrupted token and a 400 on every click.
        link["token"].ShouldMatch("^[A-Za-z0-9_-]+$");

        return new ResetLink(
            delivery.UserId,
            EmailLinkParsing.BrowserDecodeQueryValue(link["uid"]),
            EmailLinkParsing.BrowserDecodeQueryValue(link["token"]));
    }

    // uid and token as raw STRINGS, exactly as the browser hands them over — see the class summary.
    private Task<HttpResponseMessage> ResetAsync(
        string uid, string token, string newPassword, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/reset-password", new { uid, token, newPassword }, ct);

    private Task<HttpResponseMessage> ResetAsync(
        ResetLink link, string newPassword, CancellationToken ct)
        => ResetAsync(link.Uid, link.Token, newPassword, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, string password, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, ct);

    // Per-request Authorization so the before/after session checks never clobber a shared default header.
    private async Task<HttpResponseMessage> GetMeAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return await _client.SendAsync(req, ct);
    }

    [Fact]
    public async Task POST_reset_password_with_the_emitted_link_returns_204_and_swaps_the_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-ok-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var link = await EmittedResetLinkAsync(email, ct);
        var response = await ResetAsync(link, EndToEndPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The password actually changed: the NEW one logs in, the OLD one does not.
        (await LoginAsync(email, EndToEndPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LoginAsync(email, AuthTestHelpers.DefaultTestPassword, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_reset_password_with_an_already_used_link_returns_400()
    {
        // Single-use, and it comes from the same verification order the class summary describes: the
        // security stamp the token is bound to rotates on SUCCESS, so a completed reset kills the link.
        // Contrast /verify-email, which is deliberately idempotent on a double-click — there the token
        // grants nothing beyond an activation that has already happened, here it grants the account.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-single-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var link = await EmittedResetLinkAsync(email, ct);

        (await ResetAsync(link, SingleUsePassword, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replay = await ResetAsync(link, SingleUsePassword, ct);

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await replay.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.InvalidPasswordResetToken");
    }

    [Theory]
    [InlineData(true, "not-a-real-token")]      // well-formed Base64Url, wrong token
    [InlineData(true, "%%%not-base64url%%%")]   // not decodable at all (FormatException)
    [InlineData(false, "not-a-real-token")]     // no such account
    public async Task POST_reset_password_with_a_rejected_token_returns_uniform_400(
        bool accountExists, string token)
    {
        // The anti-enumeration invariant of the APPLY step. Unknown user, malformed token and wrong
        // token must be one answer: distinguishing them turns a public endpoint into an account-existence
        // oracle. The uid arm matters most — "no such account" is the one rejection whose cause is the
        // attacker's actual question.
        var ct = TestContext.Current.CancellationToken;

        Guid uid;
        if (accountExists)
        {
            var email = $"rp-badtoken-{Guid.NewGuid()}@example.se";
            await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
            uid = (await EmittedResetLinkAsync(email, ct)).UserId;
        }
        else
        {
            uid = Guid.NewGuid();
        }

        var response = await ResetAsync(uid.ToString("D"), token, RejectedTokenPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.InvalidPasswordResetToken");
    }

    [Fact]
    public async Task POST_reset_password_with_a_breached_password_returns_400_and_leaves_the_link_usable()
    {
        // The load-bearing half is the SECOND request, not the first. Identity verifies the token before
        // it runs the password validators, so a password rejection must leave the stamp unrotated and the
        // link alive. Get that wrong and a user who picks a breached password on a recovery link — the
        // one credential they still have — is locked out and must start over, during the exact incident
        // (a leaked password) that sent them here.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-pwned-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        _factory.BreachChecks.SetVerdict(BreachedPassword, BreachCheckVerdict.Breached);

        var link = await EmittedResetLinkAsync(email, ct);

        var rejected = await ResetAsync(link, BreachedPassword, ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // The rule is NAMED rather than collapsed into the uniform token failure, and that asymmetry is
        // safe for a measured reason: this arm is reachable only by someone already holding a valid
        // token, so it discloses nothing they do not have — and a real user needs to know what to fix.
        (await rejected.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.PwnedPassword");

        // THE SAME LINK, a clean password: still accepted.
        (await ResetAsync(link, CleanRetryPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(email, CleanRetryPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_reset_password_with_a_too_short_password_returns_400_naming_the_password()
    {
        // A VALID link, so the password is the only thing wrong. The rejection arrives from
        // ResetPasswordCommandValidator's shared Password() rule (NotEmpty + MinimumLength 12) rather
        // than from Identity's RequiredLength — the two floors are deliberately the same number, so the
        // FluentValidation arm always fires first and Auth.PasswordTooShort is not reachable from the
        // wire on this endpoint. That is why this asserts the validation SHAPE instead of a title.
        //
        // What it pins is the property that actually matters to the user: a too-short password must be
        // reported as a password problem, never as a bad link. The latter would tell someone holding a
        // perfectly good link to go request another one.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-short-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var link = await EmittedResetLinkAsync(email, ct);

        var response = await ResetAsync(link, TooShortPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("errors", out var errors).ShouldBeTrue(
            "a too-short password is a validation rejection, not a ProblemDetails token rejection");
        errors.EnumerateObject().Select(p => p.Name).ShouldContain(
            name => name.Equals("NewPassword", StringComparison.OrdinalIgnoreCase),
            "the rejected field is the new password, not the link");
    }

    [Fact]
    public async Task POST_reset_password_invalidates_other_sessions_and_issues_none_of_its_own()
    {
        // C6 through a RECOVERY vector. Two properties in one test because each is unsafe without the
        // other: tearing every session down matters precisely because the actor is anonymous, and
        // issuing no session matters precisely because the sessions being torn down might be the
        // attacker's. Minting one for whoever opened the link would turn recovery into login.
        //
        // The Redis store is independent of Identity's SecurityStamp, so the stamp rotation inside
        // ResetPasswordAsync does NOT reach it — the endpoint's InvalidateAllForUserAsync is the only
        // mechanism, and this runs against the real Testcontainers Redis rather than a fake.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-sessions-{Guid.NewGuid()}@example.se";
        var deviceA = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var deviceB = await AuthTestHelpers.LoginAndGetSessionIdAsync(_client, email, ct: ct);

        // Both authenticate BEFORE the reset — without this the 401s below are satisfiable by sessions
        // that never worked.
        (await GetMeAsync(deviceA, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetMeAsync(deviceB, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var link = await EmittedResetLinkAsync(email, ct);
        var response = await ResetAsync(link, SessionPassword, ct);

        // 204 with an empty body: no sessionId, nothing to carry a session on.
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty(
            "a reset issues no session — the user logs in with the new password");

        (await GetMeAsync(deviceA, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetMeAsync(deviceB, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a password reset must log the account out everywhere");
    }

    [Fact]
    public async Task POST_reset_password_writes_User_PasswordReset_audit_with_null_actor()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-audit-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var link = await EmittedResetLinkAsync(email, ct);

        (await ResetAsync(link, AuditPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Queried by AggregateId, not UserId — the load-bearing difference from an authenticated
        // command's audit, and the same shape as the #714 verify-email row: the resetter is logged out by
        // definition, so the actor is null while the TARGET is known. A completed credential mutation on
        // a recovery path is the row an incident review reads first.
        var auditEntries = await db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.AggregateId == link.UserId && e.EventType == "User.PasswordReset")
            .ToListAsync(ct);

        auditEntries.Count.ShouldBe(1, "exactly one User.PasswordReset row per completed reset");
        auditEntries[0].AggregateType.ShouldBe("User");
        auditEntries[0].UserId.ShouldBeNull("the resetter is anonymous, so the audit actor is null");
    }

    [Fact]
    public async Task POST_reset_password_sends_the_password_changed_notice()
    {
        // The breach-detection control (OWASP ASVS V2.5 / NIST SP 800-63B). A reset hands the account to
        // whoever holds the inbox, so this mail is the one moment a real owner can notice a reset they
        // did not perform while they can still act on it. It goes to the address ON RECORD, which is why
        // the recipient is asserted rather than merely the kind.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-notice-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);
        var link = await EmittedResetLinkAsync(email, ct);

        (await ResetAsync(link, NoticePassword, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        _factory.Emails.Sent.Count(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.PasswordChangedNotice)
            .ShouldBe(1, "exactly one security notice per completed reset");
    }
}
