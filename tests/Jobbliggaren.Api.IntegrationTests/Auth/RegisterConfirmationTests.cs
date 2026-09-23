using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #714 — email-confirmation-first registration (POST /api/v1/auth/register with
/// Auth:RequireEmailConfirmation ON). The whole point is to close the 200-vs-400 account-enumeration
/// status oracle, so the load-bearing assertions are the PARITY tests: a fresh and a taken address are
/// indistinguishable on both status AND body. Runs against a flag-ON host over the ApiFactory's shared
/// Testcontainers + recording IEmailSender.
/// </summary>
[Collection("Api")]
public class RegisterConfirmationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateEmailConfirmationClient();

    private const string StrongPassword = "T3stlosen123456";

    private Task<HttpResponseMessage> RegisterAsync(
        string email, string password, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password, acceptTerms = true },
            ct);

    [Fact]
    public async Task POST_register_fresh_returns_202_no_session_and_queues_confirmation_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"regconf-fresh-{Guid.NewGuid()}@example.com";

        var response = await RegisterAsync(email, StrongPassword, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty("202 carries no session-id body");

        // The out-of-band confirmation link is queued to the fresh address (the only signal).
        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.EmailConfirmation);
        // A fresh signup does NOT get an account-exists notice.
        _factory.Emails.Sent.ShouldNotContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.AccountExistsNotice);
    }

    [Fact]
    public async Task POST_register_taken_returns_202_and_queues_account_exists_notice()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"regconf-taken-{Guid.NewGuid()}@example.com";

        // First registration creates the account (and queues a confirmation link).
        (await RegisterAsync(email, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Second registration for the SAME address is a duplicate — swallowed to the same 202, with an
        // out-of-band account-exists notice instead of a confirmation link.
        var second = await RegisterAsync(email, StrongPassword, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty();

        _factory.Emails.Sent.ShouldContain(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.AccountExistsNotice);
    }

    [Fact]
    public async Task POST_register_fresh_and_taken_are_indistinguishable_on_status_and_body()
    {
        // THE anti-enumeration invariant (CTO-bind Risk 1): for a fixed strong password, a taken and a
        // fresh address must produce byte-identical responses (status + body). If they diverge, the
        // status oracle is re-opened.
        var ct = TestContext.Current.CancellationToken;
        var takenEmail = $"regconf-parity-taken-{Guid.NewGuid()}@example.com";
        var freshEmail = $"regconf-parity-fresh-{Guid.NewGuid()}@example.com";

        // Make takenEmail exist first.
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var takenResponse = await RegisterAsync(takenEmail, StrongPassword, ct);
        var freshResponse = await RegisterAsync(freshEmail, StrongPassword, ct);

        takenResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        freshResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        takenResponse.StatusCode.ShouldBe(freshResponse.StatusCode);

        var takenBody = await takenResponse.Content.ReadAsStringAsync(ct);
        var freshBody = await freshResponse.Content.ReadAsStringAsync(ct);
        takenBody.ShouldBe(freshBody, "a taken and a fresh address must be indistinguishable on the body");
        takenBody.ShouldBeNullOrEmpty("neither response carries a session-id (no instant login)");
    }

    [Fact]
    public async Task POST_register_breached_password_returns_identical_400_for_fresh_and_taken()
    {
        // CTO-bind Risk 1 (breached-vs-duplicate ordering): a breached password is
        // credential-dependent, NOT existence-dependent — Identity validates the password BEFORE
        // uniqueness, so a taken and a fresh address BOTH get the same Auth.PwnedPassword 400. This
        // pins that no breached-vs-duplicate status oracle exists.
        //
        // It is no longer the ONLY register 400 under the flag: #1117 added the display-name
        // personnummer refusal, which is input-dependent and existence-independent for the same
        // reason class, and is pinned by its own parity test below. The invariant this file defends
        // is not "exactly one 400 exists" but "every reachable 400 is existence-INDEPENDENT".
        var ct = TestContext.Current.CancellationToken;
        var breachedPassword = $"Breached-{Guid.NewGuid():N}";
        _factory.BreachChecks.SetVerdict(breachedPassword, BreachCheckVerdict.Breached);

        // Make an address exist (with a strong password), then re-register it with the breached one.
        var takenEmail = $"regconf-breach-taken-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var freshEmail = $"regconf-breach-fresh-{Guid.NewGuid()}@example.com";

        var takenBreached = await RegisterAsync(takenEmail, breachedPassword, ct);
        var freshBreached = await RegisterAsync(freshEmail, breachedPassword, ct);

        takenBreached.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        freshBreached.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var takenTitle = (await takenBreached.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("title").GetString();
        var freshTitle = (await freshBreached.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("title").GetString();

        takenTitle.ShouldBe("Auth.PwnedPassword");
        freshTitle.ShouldBe(takenTitle, "a breached password is identical for a taken and a fresh address");
    }

    [Fact]
    public async Task POST_register_fresh_stamps_the_job_seeker_row_on_the_202_path()
    {
        // #1736 (ADR 0142 D6). The 202 branch mints no session and returns an empty body, so nothing
        // on the wire says whether the acceptance was recorded — and the stamp is written BEFORE the
        // confirmation send, which is the ordering that makes this branch a stamped one. An account
        // that exists with no acceptance row is precisely the Art. 5(2) gap the column exists to close.
        var ct = TestContext.Current.CancellationToken;
        var email = $"regconf-terms-{Guid.NewGuid()}@example.com";

        (await RegisterAsync(email, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        user.ShouldNotBeNull();
        var seeker = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .JobSeekers.SingleAsync(js => js.UserId == user.Id, ct);

        seeker.TermsAcceptance.ShouldNotBeNull();
        seeker.TermsAcceptance.AcceptedAt.ShouldNotBe(default);
        seeker.TermsAcceptance.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        seeker.TermsAcceptance.PrivacyPolicyVersion
            .ShouldBe(TermsAcceptance.CurrentPrivacyPolicyVersion);
    }

    [Fact]
    public async Task POST_register_with_accept_terms_false_is_identical_for_a_fresh_and_a_taken_address()
    {
        // #1736 (ADR 0142 D6). The terms refusal is a validator rejection that runs before the handler
        // and reads only the flag, so it cannot depend on whether the address exists. Pinned the way
        // this file pins every other response: move the refusal into the handler after the
        // taken-address branch and this goes 400-for-fresh / 202-for-taken — the #714 oracle.
        var ct = TestContext.Current.CancellationToken;
        var taken = $"regconf-terms-taken-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(taken, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var forTaken = await RegisterWithoutTermsAsync(taken, ct);
        var forFresh = await RegisterWithoutTermsAsync(
            $"regconf-terms-fresh-{Guid.NewGuid()}@example.com", ct);

        forTaken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        forFresh.StatusCode.ShouldBe(forTaken.StatusCode);
        (await forFresh.Content.ReadAsStringAsync(ct))
            .ShouldBe(await forTaken.Content.ReadAsStringAsync(ct));
    }

    private Task<HttpResponseMessage> RegisterWithoutTermsAsync(string email, CancellationToken ct)
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword, displayName = "Test User", acceptTerms = false },
            ct);

    // NOTE: send-failure symmetry (CTO-bind Risk 1 — a transport fault must yield the same response for
    // the fresh and taken branches) is pinned at the UNIT level in
    // RegisterCommandHandlerTests.Handle_FlagOn_SendFaultIsIndistinguishableBetweenFreshAndTakenAddresses,
    // and end to end in OrphanedIdentityActivationTests
    // .Registration_send_fault_answers_identically_for_a_fresh_and_a_taken_address. Both branches now
    // SWALLOW the fault and answer an identical 202 — #1349 reversed the previous "propagate uncaught,
    // identical 500" shape, because propagating rolled the JobSeeker back and left an orphaned Identity
    // row. Neither needs an extra WebApplicationFactory host (which would spin another EF service
    // provider and trip the process-wide ManyServiceProvidersCreatedWarning across the shared
    // [Collection("Api")]) — the integration half reuses this factory's own flag-ON host.
}
