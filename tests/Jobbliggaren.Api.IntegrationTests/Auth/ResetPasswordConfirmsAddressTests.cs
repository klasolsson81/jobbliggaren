using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1303 — a successful password reset RECORDS the address as confirmed. The token reaching
/// <c>ResetPasswordAsync</c> was mailed to that address, which is the proof <c>ConfirmEmailAsync</c> and
/// <c>ChangeEmailAsync</c> already accept, so the reset is a third writer of an existing rule.
/// <para>
/// Its own class rather than more cases in <see cref="ResetPasswordTests"/>: that class pins the #1171
/// flow against the BASE (flag-OFF) host and says so in its summary, while the user-visible half of this
/// behaviour is only observable with the login gate ON. Both hosts here are the ApiFactory's CACHED,
/// shared ones (<c>CreateClient</c> and <c>CreateEmailConfirmationClient</c>), so this adds no
/// <c>WebApplicationFactory</c> and does not move #1190's ceiling. Re-measure the sharing with:
/// <c>grep -rn "CreateEmailConfirmationClient" tests/</c>.
/// </para>
/// <para>
/// Three tests, because the behaviour has parts that fail independently:
/// <list type="bullet">
/// <item><b>Flag ON</b> — the end-to-end outcome #1303 was filed for: a user who registers, never
/// confirms, and resets can log in afterwards. The in-test 403 BEFORE the reset is the counterfactual,
/// not decoration: without it a green run cannot tell "the reset confirmed the address" from "the login
/// gate was never armed on this host".</item>
/// <item><b>Flag OFF</b> — the same write, pinned on the column. Flag-OFF has no HTTP-observable
/// difference by construction (nothing reads <c>EmailConfirmed</c> when the gate is off), so this is the
/// only way to hold the flag-INDEPENDENCE decision: the flag governs enforcement at login, not whether
/// the fact is recorded. Without this test a later "be symmetric with the resend gate" refactor would
/// add a flag branch and no test would go red.</item>
/// <item><b>Rejected token</b> — the write's POSITION rather than its polarity. The two above stay green
/// if the write is hoisted into the request half or above the token verification, and both of those are
/// unauthenticated confirm-anyone primitives.</item>
/// </list>
/// </para>
/// </summary>
[Collection("Api")]
public class ResetPasswordConfirmsAddressTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _confirmationClient = factory.CreateEmailConfirmationClient();
    private readonly HttpClient _client = factory.CreateClient();

    private const string BaseUrl = "https://jobbliggaren.se";

    // The bootstrap password is the suite's shared default; the breach-check stub is a dictionary keyed
    // by password and shared across the whole "Api" collection, so the three NEW passwords below it are
    // unique to this class.
    private const string RegisterPassword = AuthTestHelpers.DefaultTestPassword;
    private const string ConfirmsFlagOnPassword = "BekraftatL0sen1234";  // gitleaks:allow
    private const string ConfirmsFlagOffPassword = "OberoendeL0sen123";  // gitleaks:allow
    private const string RejectedTokenPassword = "AvvisadL0sen12345";    // gitleaks:allow

    /// <summary>The (uid, token) pair a real inbox receives, already browser-decoded.</summary>
    private sealed record ResetLink(string Uid, string Token);

    // Minted through the PRODUCTION eligibility+mint port and rendered by the PRODUCTION template, then
    // read back out of the rendered body — the same shape ResetPasswordTests uses, and for the same
    // reason: a hand-built pair would assert an outcome off a link production never emits (§5 Tests).
    // The mint runs in the BASE host's scope while the flag-ON test drives the flag-ON client; the two
    // share the Testcontainers database and the Data-Protection keyring, which VerifyEmailTests already
    // relies on (it mints on the base host and validates through the confirmation client).
    private async Task<ResetLink> EmittedResetLinkAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var delivery = await accounts.TryPreparePasswordResetAsync(email, ct);
        delivery.ShouldNotBeNull();

        var rendered = EmailTemplates.PasswordReset(
            BaseUrl, new PasswordResetEmail(delivery.UserId, delivery.UrlSafeToken));
        var link = EmailLinkParsing.ExtractLinkQuery(rendered.PlainTextBody, "/aterstall-losenord");

        return new ResetLink(
            EmailLinkParsing.BrowserDecodeQueryValue(link["uid"]),
            EmailLinkParsing.BrowserDecodeQueryValue(link["token"]));
    }

    private async Task<bool> IsEmailConfirmedAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return user.EmailConfirmed;
    }

    // uid and token as raw STRINGS, exactly as the browser hands them over (#981).
    private static Task<HttpResponseMessage> ResetAsync(
        HttpClient client, ResetLink link, string newPassword, CancellationToken ct)
        => client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new { uid = link.Uid, token = link.Token, newPassword },
            ct);

    [Fact]
    public async Task POST_reset_password_confirms_the_address_so_an_unconfirmed_user_can_log_in()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-confirms-{Guid.NewGuid()}@example.se";

        // Flag-ON registration: 202, no session, EmailConfirmed=false.
        var register = await _confirmationClient.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = RegisterPassword, displayName = "Test User" },
            ct);
        register.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // THE COUNTERFACTUAL. The login gate is armed on this host and this account is behind it, so the
        // final 200 below cannot be satisfied by a host that simply never checks.
        var blocked = await _confirmationClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = RegisterPassword }, ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe(AuthErrorCodes.EmailNotConfirmed);

        var link = await EmittedResetLinkAsync(email, ct);
        var reset = await ResetAsync(_confirmationClient, link, ConfirmsFlagOnPassword, ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The dead end #1303 was filed for: before this change the same request answered 403
        // Auth.EmailNotConfirmed, and /resend-confirmation was the only way out.
        var login = await _confirmationClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { email, password = ConfirmsFlagOnPassword }, ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task POST_reset_password_sets_EmailConfirmed_even_with_the_confirmation_flag_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-confirms-flagoff-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        var link = await EmittedResetLinkAsync(email, ct);

        // Pre-state, and deliberately AFTER the mint rather than before it. Placed before, it would only
        // say that registration leaves the column false; placed here it also says the REQUEST half wrote
        // nothing — and that half is reached from an unauthenticated endpoint taking an arbitrary
        // address, so a write there would confirm anyone on demand.
        (await IsEmailConfirmedAsync(email, ct)).ShouldBeFalse();

        var reset = await ResetAsync(_client, link, ConfirmsFlagOffPassword, ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await IsEmailConfirmedAsync(email, ct)).ShouldBeTrue(
            "the flag governs enforcement at login, not whether the confirmed fact is recorded");
    }

    [Fact]
    public async Task POST_reset_password_with_a_rejected_token_leaves_the_address_unconfirmed()
    {
        // The write's POSITION, not its polarity — the property the two tests above cannot fail on.
        // /reset-password is public and takes an arbitrary uid, so a write hoisted above the token
        // verification would be an unauthenticated confirm-anyone primitive: 400 on the way out, address
        // confirmed on the way through. ResetPasswordTests pins the 400; this pins that nothing was
        // recorded behind it.
        var ct = TestContext.Current.CancellationToken;
        var email = $"rp-confirms-badtoken-{Guid.NewGuid()}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

        // A real uid with a junk token. The token is in the Base64Url alphabet on purpose: a malformed
        // one short-circuits at the decode and would leave a write placed after it uncaught.
        var uid = (await EmittedResetLinkAsync(email, ct)).Uid;
        var rejected = await ResetAsync(
            _client, new ResetLink(uid, "not-a-real-token"), RejectedTokenPassword, ct);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await IsEmailConfirmedAsync(email, ct)).ShouldBeFalse(
            "a reset that verified no token proves no inbox control, so it records nothing");
    }
}
