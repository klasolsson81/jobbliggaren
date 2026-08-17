using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1349 — a registration whose confirmation send fails leaves an ORPHANED Identity row (Identity user
/// committed, <c>JobSeeker</c> rolled back), and the account can then be ACTIVATED: every screen reports
/// success, the user is told the account is active, and the #508 orphan sweep deletes it hours later with
/// no notice. Walked end to end on dev 2026-08-16 (<c>users=1, confirmed=1, job_seekers=0</c>, same id).
/// <para>
/// <b>The premise is produced by production, never seeded.</b> The orphan here comes out of the real
/// <c>POST /auth/register</c> against a sender whose send THROWS — the shape a provider outage emits
/// (<c>ScalewayEmailSender</c> wraps every transport and 4xx/5xx fault in
/// <see cref="EmailDeliveryException"/> and lets it escape). <c>RegisterCommandHandler</c> commits the
/// Identity user in its own boundary and sends as its FINAL action, so the throw rolls the
/// not-yet-committed <c>JobSeeker</c> back. That is the documented design
/// (<c>RegisterCommandHandler.cs:99-102</c>, <c>registration-gate.md</c> §5), which is exactly why the
/// state is reachable rather than hypothetical — and why CLAUDE.md §5 <c>Tests:</c> is satisfied without
/// a hand-seeded row.
/// </para>
/// </summary>
[Collection("Api")]
public class OrphanedIdentityActivationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string StrongPassword = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword, displayName = "Test User" },
            ct);

    private Task<HttpResponseMessage> ResendAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email }, ct);

    private Task<HttpResponseMessage> VerifyAsync(Guid uid, string token, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { uid, token }, ct);

    private Task<HttpResponseMessage> LoginAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = StrongPassword }, ct);

    /// <summary>
    /// Drives the REAL registration path against a throwing sender and returns the orphaned user's id.
    /// The <see cref="EmailDeliveryException"/> is answered as a 500 rather than surfaced to the caller:
    /// <c>Program.cs</c>'s own middleware matches named exception types only and does not match this one,
    /// so the request falls through to the developer exception page the test host composes. Production
    /// answers the same 500 by a different route (no dev page, unhandled → Kestrel), and either way the
    /// row left behind is identical — which is what this models.
    /// </summary>
    private async Task<Guid> RegisterWithFailingSendAsync(string email, CancellationToken ct)
    {
        using (_factory.Emails.FailingSends())
        {
            var refused = await RegisterAsync(email, ct);
            refused.StatusCode.ShouldBe(
                HttpStatusCode.InternalServerError,
                "the send throws as the handler's final action, so the request fails");
        }

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull("the Identity user is committed in its own boundary before the send");
        return user.Id;
    }

    private async Task<bool> HasJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // IgnoreQueryFilters so a soft-deleted profile still counts as present — the question here is
        // whether the account owns a profile row at all, not whether it is active.
        return await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(js => js.UserId == userId, ct);
    }

    private async Task<bool> IsEmailConfirmedAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();
        return user.EmailConfirmed;
    }

    // A real confirmation token for the user, minted exactly as the production wrapper does
    // (UserAccountService.GenerateEmailConfirmationTokenAsync). Same seam VerifyEmailTests uses: the
    // token is generated through the host UserManager, never parsed out of the recording sender.
    private async Task<string> MintUrlSafeTokenAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.ShouldNotBeNull();
        var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(rawToken));
    }

    [Fact]
    public async Task Registration_whose_confirmation_send_throws_leaves_an_Identity_row_with_no_job_seeker()
    {
        // THE PREMISE, measured rather than assumed. Every other assertion in this class rests on this
        // state being reachable from src/, so it is pinned on its own: if registration ever becomes
        // atomic across the two boundaries (or commits before it sends), this test fails and the rest
        // of the file is describing a state production can no longer produce.
        var ct = TestContext.Current.CancellationToken;
        var email = $"orphan-premise-{Guid.NewGuid()}@example.com";

        var userId = await RegisterWithFailingSendAsync(email, ct);

        (await HasJobSeekerAsync(userId, ct)).ShouldBeFalse(
            "the JobSeeker was never committed — the send is the handler's final action and it threw");
        (await IsEmailConfirmedAsync(userId)).ShouldBeFalse(
            "no confirmation link was ever delivered");
    }

    [Fact]
    public async Task An_orphaned_account_never_becomes_login_capable()
    {
        // THE DEFECT (#1349). The user's own instinct completes the trap: retry the registration, read
        // "you already have an account", follow "Logga in", hit "skicka ny kod", confirm, and stop —
        // believing they are done. They are not: the account owns no profile, cannot be deleted through
        // DeleteAccountCommandHandler (it answers NotFound), and is swept at 04:00 UTC.
        //
        // The invariant is stated on the END STATE, but read what it actually pins: the token below is
        // minted through the host UserManager, so a fix that only refuses the RESEND does not turn this
        // green. That is not a flaw in the test — holding a valid token for an orphaned row is a
        // production leg of its own (registration mints and sends BEFORE the JobSeeker commits, so a
        // send that succeeds against a commit that fails delivers exactly such a link). What this pins
        // is the property, not one delivery route into it.
        var ct = TestContext.Current.CancellationToken;
        var email = $"orphan-activation-{Guid.NewGuid()}@example.com";

        var userId = await RegisterWithFailingSendAsync(email, ct);
        (await HasJobSeekerAsync(userId, ct)).ShouldBeFalse("premise: the row is orphaned");

        // Step 1 — the account-exists notice tells the user to log in. (Sends normally now: the
        // FailingSends scope closed with the registration above.)
        (await RegisterAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Step 2 — login is refused on EmailNotConfirmed, and that surface offers "skicka ny kod".
        (await LoginAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Step 3 — the resend.
        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Step 4 — following the delivered link.
        var token = await MintUrlSafeTokenAsync(userId);
        await VerifyAsync(userId, token, ct);

        // THE ASSERTION. An account that can log in must own a profile; an account that owns no profile
        // must not be able to log in. Either half satisfies it — what must never happen is the pair.
        //
        // Stated TOTALLY rather than as an `if` around the second measurement, deliberately: a branch on
        // the login status is fail-open, because any third status (a 500 from an unrelated regression)
        // would skip the assertion and report green. Both values are measured unconditionally and both
        // are named in the failure message.
        var loginStatus = (await LoginAsync(email, ct)).StatusCode;
        var hasJobSeeker = await HasJobSeekerAsync(userId, ct);

        (hasJobSeeker || loginStatus != HttpStatusCode.OK).ShouldBeTrue(
            $"a login-capable account must own a job seeker — measured login={loginStatus}, "
            + $"jobSeeker={hasJobSeeker}. Otherwise the user is told the account is active while owning "
            + "nothing, and the #508 sweep deletes it at 04:00 UTC with no notice (#1349)");
    }
}
