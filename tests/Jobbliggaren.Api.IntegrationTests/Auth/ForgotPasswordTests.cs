using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1171 — password reset, REQUEST step (POST /api/v1/auth/forgot-password). PUBLIC: the requester has
/// lost access by definition, so there is nothing to authenticate with, and every load-bearing assertion
/// here is therefore an anti-enumeration invariant. A known address, an unknown one and a cooled repeat
/// are indistinguishable on status AND body (always 202); the reset link is the only differentiator and
/// it goes out-of-band, to an inbox the requester must already control. Verifies:
/// <list type="bullet">
/// <item>Known address → 202 and exactly ONE PasswordReset mail to that address</item>
/// <item>Unknown address → 202 and no mail at all</item>
/// <item>Known and unknown are byte-identical on status AND body (the structural clone of
/// <c>ResendConfirmationTests.Resend_unconfirmed_confirmed_and_nonexistent_are_indistinguishable_on_status_and_body</c>)</item>
/// <item>A within-cooldown repeat is still 202 (never 409/429 — a visible throttle would answer
/// differently for a recently-requested address) but sends nothing more</item>
/// <item>Malformed email → 400, which is existence-INDEPENDENT and so not an oracle</item>
/// <item>Sender cannot deliver → 503 with ProblemDetails title <c>Auth.EmailDeliveryUnavailable</c>,
/// no mail recorded, and the refusal does not burn the requester's cooldown window</item>
/// <item><b>The one with no sibling precedent:</b> under an incapable sender a KNOWN and an UNKNOWN
/// address get the SAME 503 — same status, same title. The handler's capability check is its first
/// statement and reads no input; reorder it below the account lookup and the 503 becomes reachable only
/// for existing accounts, i.e. precisely the existence oracle the uniform 202 exists to prevent. Nothing
/// else in this suite would go red on that reordering.</item>
/// </list>
/// Every test runs on the BASE <c>factory.CreateClient()</c>. The 503 cases flip capability in place via
/// <c>factory.Emails.Incapable()</c> rather than on a dedicated host, because a fourth
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> would breach EF's
/// process-global <c>ManyServiceProvidersCreatedWarning</c> ceiling and fell whichever collection fixture
/// initialises after it (#1190). Runs over the ApiFactory's shared Testcontainers Postgres/Redis (so the
/// cooldown is the real Redis gate) + the recording <see cref="RecordingEmailSender"/>.
/// </summary>
[Collection("Api")]
public class ForgotPasswordTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> ForgotAsync(string? email, CancellationToken ct)
        => _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }, ct);

    // Registration is the only account-creation path on the base host, and it mails nothing while
    // Auth:RequireEmailConfirmation is OFF (ApiFactory pins it so) — the session it returns is
    // discarded here, so the ONLY mail this class can observe for a recipient is the reset link's.
    private async Task CreateAccountAsync(string email, CancellationToken ct)
        => _ = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, email, ct: ct);

    private int ResetMailCount(string email)
        => _factory.Emails.Sent.Count(e =>
            e.ToEmail == email && e.Kind == RecordedEmailKind.PasswordReset);

    [Fact]
    public async Task POST_forgot_password_for_known_address_returns_202_and_sends_one_reset_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"fp-known-{Guid.NewGuid()}@example.se";
        await CreateAccountAsync(email, ct);

        var response = await ForgotAsync(email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(ct)).ShouldBeNullOrEmpty("202 carries no body");
        ResetMailCount(email).ShouldBe(1);
    }

    [Fact]
    public async Task POST_forgot_password_for_unknown_address_returns_202_and_sends_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"fp-nobody-{Guid.NewGuid()}@example.se";

        var response = await ForgotAsync(email, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ResetMailCount(email).ShouldBe(0, "no account, so there is nothing to send a link for");
    }

    [Fact]
    public async Task POST_forgot_password_known_and_unknown_are_indistinguishable_on_status_and_body()
    {
        // THE anti-enumeration invariant. Two-way rather than the resend sibling's three-way, because
        // this surface has only two account states to hide: the reset path deliberately does NOT consult
        // RequireEmailConfirmation (holding the emailed token proves inbox control, which is what
        // confirmation proves), so a confirmed and an unconfirmed account are the same case here.
        var ct = TestContext.Current.CancellationToken;
        var known = $"fp-parity-known-{Guid.NewGuid()}@example.se";
        var nobody = $"fp-parity-nobody-{Guid.NewGuid()}@example.se";
        await CreateAccountAsync(known, ct);

        var knownResponse = await ForgotAsync(known, ct);
        var nobodyResponse = await ForgotAsync(nobody, ct);

        knownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        nobodyResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var knownBody = await knownResponse.Content.ReadAsStringAsync(ct);
        var nobodyBody = await nobodyResponse.Content.ReadAsStringAsync(ct);
        knownBody.ShouldBe(nobodyBody);
        knownBody.ShouldBeNullOrEmpty("no response carries any distinguishing body");

        // The counterfactual, without which the parity above is satisfiable by two addresses that are
        // both unknown (a silently broken registration would produce exactly that). The two requests
        // above DID differ in the property the response must not reveal.
        ResetMailCount(known).ShouldBe(1);
        ResetMailCount(nobody).ShouldBe(0);
    }

    [Fact]
    public async Task POST_forgot_password_within_cooldown_returns_202_but_sends_only_one_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"fp-cooldown-{Guid.NewGuid()}@example.se";
        await CreateAccountAsync(email, ct);

        (await ForgotAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // An immediate repeat for the SAME address is inside the 60s per-target window
        // (CooldownScopes.PasswordReset) → still 202, never a 409 or 429: a visible throttle on an
        // unauthenticated surface answers differently for an address someone recently requested, which
        // is an enumeration oracle assembled out of the anti-abuse control itself.
        (await ForgotAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        ResetMailCount(email).ShouldBe(1, "the second request is cooled — same answer, no second mail");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task POST_forgot_password_with_malformed_email_returns_400(string email)
    {
        // A format-level 400 is existence-INDEPENDENT (identical for any malformed input), so it is not
        // an enumeration oracle; every well-formed address funnels to the uniform 202 above.
        var ct = TestContext.Current.CancellationToken;

        (await ForgotAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_forgot_password_returns_503_when_the_sender_cannot_deliver_and_202_when_it_can()
    {
        // #1087's gate applied to a flow whose success is DEFINED by delivery: the password changes only
        // when the emailed link is opened, so a dropped send leaves someone who has already lost access
        // with "check your inbox" and no link. NullEmailSender is the live default outside
        // Development/Test, so this is the ordinary configuration, not an edge case.
        var ct = TestContext.Current.CancellationToken;
        var email = $"fp-nodeliver-{Guid.NewGuid()}@example.se";
        await CreateAccountAsync(email, ct);

        HttpResponseMessage refused;
        using (_factory.Emails.Incapable())
        {
            refused = await ForgotAsync(email, ct);
        }

        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        // The PARSED ProblemDetails title, not a body substring: a Redis outage also produces a 503 on
        // this route, so a substring match would not prove the contract a client discriminates on
        // (the frontend whitelists the title exactly).
        (await refused.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("title").GetString().ShouldBe("Auth.EmailDeliveryUnavailable");

        // Nothing was attempted and nothing recorded — the refusal is up front, not a swallowed send.
        ResetMailCount(email).ShouldBe(0);

        // The crossing arm, in the same test for the reason ChangeEmailTests states: the 503 assertion
        // alone is equally satisfied by a gate stuck permanently on, and splitting the arms lets a later
        // tidy-up delete the half that carries the proof. It also establishes a second property for
        // free — the cooldown window is 60s and this call is immediate, so a 202 WITH a mail here proves
        // the refusal did not begin the window. Had the capability check been placed after the cooldown,
        // a user would be throttled out of retrying by our own misconfiguration.
        (await ForgotAsync(email, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ResetMailCount(email).ShouldBe(1, "capability restored, everything else identical");
    }

    [Fact]
    public async Task POST_forgot_password_returns_the_same_503_for_a_known_and_an_unknown_address()
    {
        // The invariant with no sibling precedent, and the reason it is worth its own test: the 503 is
        // safe ONLY because of WHERE the handler decides it. The capability check is the handler's first
        // statement and reads no input, so the 503/202 split is a property of the server's configuration,
        // settled before the submitted address is looked at. Move it below the account lookup — a
        // plausible tidy-up, since every other step in that handler needs the address — and the 503
        // becomes reachable only for accounts that exist. The endpoint would then answer 503 for a real
        // address and 202 for a fake one: a perfect existence oracle, handed out during an outage, on the
        // one surface whose entire contract is that it cannot be told apart.
        //
        // Nothing else in this suite goes red on that reordering. The uniform-202 test never runs with an
        // incapable sender, and the 503 test above uses only a known address.
        var ct = TestContext.Current.CancellationToken;
        var known = $"fp-503-known-{Guid.NewGuid()}@example.se";
        var nobody = $"fp-503-nobody-{Guid.NewGuid()}@example.se";
        await CreateAccountAsync(known, ct);

        HttpResponseMessage knownResponse, nobodyResponse;
        using (_factory.Emails.Incapable())
        {
            knownResponse = await ForgotAsync(known, ct);
            nobodyResponse = await ForgotAsync(nobody, ct);
        }

        knownResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        nobodyResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var knownProblem = await knownResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var nobodyProblem = await nobodyResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Same title and same detail (traceId legitimately differs per request), so the refusal carries
        // no signal about the address it refused.
        knownProblem.GetProperty("title").GetString().ShouldBe("Auth.EmailDeliveryUnavailable");
        nobodyProblem.GetProperty("title").GetString()
            .ShouldBe(knownProblem.GetProperty("title").GetString());
        nobodyProblem.GetProperty("detail").GetString()
            .ShouldBe(knownProblem.GetProperty("detail").GetString());

        // Neither address was mailed while the sender was incapable.
        ResetMailCount(known).ShouldBe(0);
        ResetMailCount(nobody).ShouldBe(0);

        // The control this probe must cross. Without it the parity above is satisfiable by two addresses
        // that are both unknown, and the test would pass on a host where registration silently failed.
        // With the sender capable again, the two addresses behave DIFFERENTLY — which is exactly the
        // difference the two 503s had to conceal.
        (await ForgotAsync(known, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ForgotAsync(nobody, ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        ResetMailCount(known).ShouldBe(1, "the known address is genuinely known");
        ResetMailCount(nobody).ShouldBe(0, "the unknown address is genuinely unknown");
    }
}
