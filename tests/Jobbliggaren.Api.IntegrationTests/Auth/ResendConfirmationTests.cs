using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #733 — resend registration confirmation link (POST /api/v1/auth/resend-confirmation). The load-bearing
/// assertions are the anti-enumeration invariants: an unconfirmed-account, a confirmed-account and a
/// no-account address are indistinguishable on status AND body (always 202); a confirmation email is sent
/// ONLY for an unconfirmed account, out-of-band via the recording <see cref="RecordingEmailSender"/>; a
/// within-cooldown repeat is still 202 (never 429) but sends nothing more; and — the code-reviewer Major —
/// with the flag OFF the endpoint is a uniform no-op that mails nobody (preserving #714's prod-safe default
/// OFF). Accounts are created directly via the host UserManager (no register endpoint) so the ONLY emails
/// observed are the resend's. Runs over the ApiFactory's shared Testcontainers + recording IEmailSender.
/// </summary>
[Collection("Api")]
public class ResendConfirmationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string StrongPassword = "T3stlosen123456";

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client, string? email, CancellationToken ct)
        => client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email }, ct);

    private Task<HttpResponseMessage> ResendAsync(string? email, CancellationToken ct)
        => ResendAsync(_client, email, ct);

    // Create an Identity account directly (no register endpoint → no register-path confirmation email), so
    // the ONLY EmailConfirmation the recording sender captures for this recipient is the resend's.
    private async Task CreateAccountAsync(string email, bool confirmed)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = confirmed };
        (await userManager.CreateAsync(user, StrongPassword)).Succeeded.ShouldBeTrue();
    }

    private int ConfirmationMailCount(string email)
        => _factory.Emails.Sent.Count(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.EmailConfirmation);

    [Fact]
    public async Task Resend_for_unconfirmed_account_returns_202_and_sends_a_confirmation_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-unconf-{Guid.NewGuid()}@example.com";
        await CreateAccountAsync(email, confirmed: false);

        var response = await ResendAsync(email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty("202 carries no body");
        ConfirmationMailCount(email).ShouldBe(1);
    }

    [Fact]
    public async Task Resend_for_confirmed_account_returns_202_and_sends_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-conf-{Guid.NewGuid()}@example.com";
        await CreateAccountAsync(email, confirmed: true);

        var response = await ResendAsync(email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ConfirmationMailCount(email).ShouldBe(0, "a confirmed account needs no resend");
    }

    [Fact]
    public async Task Resend_for_nonexistent_address_returns_202_and_sends_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-nobody-{Guid.NewGuid()}@example.com";

        var response = await ResendAsync(email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ConfirmationMailCount(email).ShouldBe(0);
    }

    [Fact]
    public async Task Resend_unconfirmed_confirmed_and_nonexistent_are_indistinguishable_on_status_and_body()
    {
        // THE anti-enumeration invariant (three-way): an unconfirmed-account, a confirmed-account and a
        // no-account address must produce byte-identical responses (status + body). The only differentiator
        // is the out-of-band email (which reaches only an inbox the requester controls).
        var ct = TestContext.Current.CancellationToken;
        var unconfirmed = $"resend-parity-unconf-{Guid.NewGuid()}@example.com";
        var confirmed = $"resend-parity-conf-{Guid.NewGuid()}@example.com";
        var nobody = $"resend-parity-nobody-{Guid.NewGuid()}@example.com";
        await CreateAccountAsync(unconfirmed, confirmed: false);
        await CreateAccountAsync(confirmed, confirmed: true);

        var responses = new[]
        {
            await ResendAsync(unconfirmed, ct),
            await ResendAsync(confirmed, ct),
            await ResendAsync(nobody, ct),
        };

        foreach (var r in responses)
            r.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var bodies = new[]
        {
            await responses[0].Content.ReadAsStringAsync(ct),
            await responses[1].Content.ReadAsStringAsync(ct),
            await responses[2].Content.ReadAsStringAsync(ct),
        };
        bodies[0].ShouldBe(bodies[1]);
        bodies[1].ShouldBe(bodies[2]);
        bodies[0].ShouldBeNullOrEmpty("no response carries any distinguishing body");
    }

    [Fact]
    public async Task Resend_within_cooldown_returns_202_but_sends_only_one_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-cooldown-{Guid.NewGuid()}@example.com";
        await CreateAccountAsync(email, confirmed: false);

        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // An immediate second resend for the SAME address is within the 60s cooldown window → still 202
        // (never a 429 — anti-enumeration) but NO second send (the anti-email-bomb throttle).
        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        ConfirmationMailCount(email).ShouldBe(1);
    }

    [Fact]
    public async Task Resend_when_flag_off_returns_202_and_sends_nothing_even_for_unconfirmed_account()
    {
        // #714 prod-safe default OFF (code-reviewer Major): with Auth:RequireEmailConfirmation OFF
        // (instant-login), EVERY account is EmailConfirmed=false, so the endpoint must be a uniform no-op —
        // never mail a user whose login works. Uses the BASE (flag-OFF) host; the account is unconfirmed.
        var ct = TestContext.Current.CancellationToken;
        var email = $"resend-flagoff-{Guid.NewGuid()}@example.com";
        await CreateAccountAsync(email, confirmed: false);

        using var flagOffClient = _factory.CreateClient();
        var response = await ResendAsync(flagOffClient, email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ConfirmationMailCount(email).ShouldBe(0, "flag-OFF is a uniform no-op — nobody is mailed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Resend_malformed_email_returns_400(string email)
    {
        // A format-level 400 is existence-INDEPENDENT (identical for any malformed input) so it is not an
        // enumeration oracle; a well-formed address always funnels to the uniform 202 above.
        var ct = TestContext.Current.CancellationToken;

        (await ResendAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
