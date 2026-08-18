using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
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
        string email, string password, CancellationToken ct, string displayName = "Test User")
        => _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password, displayName },
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
    public async Task POST_register_personnummer_display_name_returns_identical_400_for_fresh_and_taken()
    {
        // #1117, and the reason this test exists is a defect the invariant itself introduced. The
        // refusal lives in the JobSeeker aggregate, which the handler can only reach AFTER
        // CreateUserAsync — and the duplicate-address branch returns the uniform 202 before that.
        // Evaluated there, one and the same request answers 202 for a taken address and 400 for a
        // fresh one: an existence-dependent status oracle, attacker-controlled, needing no valid
        // credential. The handler therefore runs the aggregate's own ValidateDisplayName BEFORE
        // CreateUserAsync. This pins the property, not the placement: any future reordering that
        // puts an input refusal after the duplicate branch fails here.
        //
        // The parity test above uses a CLEAN display name, so it is green either way — which is
        // why this needed its own case rather than a stronger assertion there.
        var ct = TestContext.Current.CancellationToken;
        var pnrDisplayName = "Anna 811218-9876";

        var takenEmail = $"regconf-pnr-taken-{Guid.NewGuid()}@example.com";
        (await RegisterAsync(takenEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var freshEmail = $"regconf-pnr-fresh-{Guid.NewGuid()}@example.com";

        var taken = await RegisterAsync(takenEmail, StrongPassword, ct, pnrDisplayName);
        var fresh = await RegisterAsync(freshEmail, StrongPassword, ct, pnrDisplayName);

        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fresh.StatusCode.ShouldBe(taken.StatusCode, "a refused display name must not reveal whether the address exists");

        var takenBody = await taken.Content.ReadAsStringAsync(ct);
        var freshBody = await fresh.Content.ReadAsStringAsync(ct);
        takenBody.ShouldBe(freshBody, "status AND body must be identical for a taken and a fresh address");

        var title = (await fresh.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct))
            .GetProperty("title").GetString();
        title.ShouldBe("JobSeeker.DisplayNamePersonnummerMustBeRemoved");

        // And the refusal left nothing behind: the fresh address is still registrable, which it
        // would not be if an Identity user had been created and then compensated away.
        (await RegisterAsync(freshEmail, StrongPassword, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

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
