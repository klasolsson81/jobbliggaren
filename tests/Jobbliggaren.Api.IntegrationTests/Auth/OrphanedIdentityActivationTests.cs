using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1349 — an ORPHANED Identity row (an account with no <c>JobSeeker</c>) must never become
/// login-capable. Measured live on dev 2026-08-16: a registration whose confirmation send failed left
/// such a row, the resend then activated it, every screen reported success, and the #508 sweep deleted
/// the account at 04:00 UTC with no notice to anyone.
/// <para>
/// <b>Two halves, and neither is correct alone</b> (senior-cto-advisor 2026-08-17). The first pins that
/// registration no longer PRODUCES the row: the send is swallowed, so the <c>JobSeeker</c> commits and
/// the account is whole. The second pins that a row which exists anyway — <c>AccountHardDeleter</c>
/// step 2h still produces one, and rows written before this change exist — is refused at the CAPABILITY
/// seam. Guarding login rather than each route into it is why <c>/verify-email</c>, the resend and the
/// #1303 password-reset write all need no change: <c>EmailConfirmed</c> on a profile-less row grants
/// nothing.
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

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return user.Id;
    }

    private async Task<bool> HasJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // IgnoreQueryFilters so a soft-deleted profile still counts as present — the question is whether
        // the account owns a profile row at all, not whether it is active.
        return await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(js => js.UserId == userId, ct);
    }

    // A real confirmation token, minted exactly as the production wrapper does
    // (UserAccountService.GenerateEmailConfirmationTokenAsync). Same seam VerifyEmailTests uses.
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
    public async Task Registration_whose_confirmation_send_fails_still_commits_the_job_seeker()
    {
        // HALF ONE — the producer is closed. The send THROWS EmailDeliveryException, which is the shape
        // a provider outage emits (ScalewayEmailSender wraps every transport and 4xx/5xx fault in that
        // type and lets it escape the adapter); this is the exact fault that produced the dev orphan.
        //
        // Before this change the throw escaped the handler, UnitOfWorkBehavior never reached its
        // SaveChangesAsync, and the tracked JobSeeker was dropped while the Identity user — committed in
        // its own boundary by UserManager — survived. Now the fault is swallowed: the request answers
        // the same uniform 202 as a successful send and the account is whole.
        var ct = TestContext.Current.CancellationToken;
        var email = $"orphan-producer-{Guid.NewGuid()}@example.com";

        using (_factory.Emails.FailingSends())
        {
            var response = await RegisterAsync(email, ct);
            response.StatusCode.ShouldBe(
                HttpStatusCode.Accepted,
                "a transport fault must not fell a registration that otherwise succeeded");
        }

        var userId = await GetUserIdAsync(email);
        (await HasJobSeekerAsync(userId, ct)).ShouldBeTrue(
            "the JobSeeker commits even though the activation mail was never delivered — no orphan (#1349)");

        // The user lost the mail, not the account, and the recovery for exactly that is the #733 resend
        // already mounted on the screen they are looking at. It works because the account is real.
        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.EmailConfirmation);
    }

    [Fact]
    public async Task Registration_send_fault_answers_identically_for_a_fresh_and_a_taken_address()
    {
        // The anti-enumeration invariant over the SAME outage. Swallowing only the confirmation send
        // would mean a fresh address answers 202 while a taken one answers 500 — the status oracle #714
        // was built to close, re-opened by the fix itself. Both arms are swallowed, so both answer 202.
        var ct = TestContext.Current.CancellationToken;
        var taken = $"orphan-parity-taken-{Guid.NewGuid()}@example.com";
        var fresh = $"orphan-parity-fresh-{Guid.NewGuid()}@example.com";

        // Make `taken` exist first, with delivery working.
        (await RegisterAsync(taken, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using (_factory.Emails.FailingSends())
        {
            // `taken` now goes down the duplicate branch (account-exists notice); `fresh` down the
            // confirmation branch. One outage, two arms.
            var takenResponse = await RegisterAsync(taken, ct);
            var freshResponse = await RegisterAsync(fresh, ct);

            takenResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            freshResponse.StatusCode.ShouldBe(
                takenResponse.StatusCode,
                "a send fault must not split the duplicate branch from the fresh one");

            var takenBody = await takenResponse.Content.ReadAsStringAsync(ct);
            var freshBody = await freshResponse.Content.ReadAsStringAsync(ct);
            takenBody.ShouldBe(freshBody);
            takenBody.ShouldBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task An_orphaned_account_is_refused_at_login_even_after_the_link_is_followed()
    {
        // HALF TWO — the capability seam. This walks the trap the issue measured: the user retries and
        // is told the account exists, follows "Logga in", hits "skicka ny kod", confirms, and believes
        // they are done. Every step still reports success, because none of them is where the defect
        // lived; login is, and it now refuses.
        //
        // WHO PRODUCES THE PREMISE (CLAUDE.md §5 Tests:). After half one, registration does not — so the
        // row is seeded here and the actor is named rather than implied:
        //   1. AccountHardDeleter step 2h (AccountHardDeleter.cs:302-309), whose own comment admits the
        //      state in writing: the domain transaction commits first, the Identity DELETE is a separate
        //      boundary after it, and "Om denna failer plockas raden upp av Steg 0 ... i nästa körning"
        //      — i.e. the row is live until the next daily run. NOT retired by this PR.
        //   2. Rows written before this change, when the confirmation send threw.
        // The pin that producer 2 is closed is the test above, in this same file.
        var ct = TestContext.Current.CancellationToken;
        var email = $"orphan-capability-{Guid.NewGuid()}@example.com";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, StrongPassword)).Succeeded.ShouldBeTrue();
        }

        var userId = await GetUserIdAsync(email);
        (await HasJobSeekerAsync(userId, ct)).ShouldBeFalse("premise: the row is orphaned");

        // The resend still delivers — deliberately. It is not the seam that grants anything, and making
        // it existence-dependent would put an account-status oracle on a public endpoint.
        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // The link still activates the row. Also deliberate: EmailConfirmed is a fact about the address,
        // and refusing here would answer "your link is invalid or expired", which is untrue.
        (await VerifyAsync(userId, await MintUrlSafeTokenAsync(userId), ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // THE ASSERTION. Confirmed, correct password, and still refused — the activation grants nothing.
        var login = await LoginAsync(email, ct);
        login.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "an account that owns no profile must not be able to log in — otherwise the user is told it "
            + "is active while owning nothing, and the #508 sweep deletes it at 04:00 UTC (#1349)");

        (await HasJobSeekerAsync(userId, ct)).ShouldBeFalse(
            "the guard REFUSES the login; it does not quietly provision a profile to let it through");
    }
}
