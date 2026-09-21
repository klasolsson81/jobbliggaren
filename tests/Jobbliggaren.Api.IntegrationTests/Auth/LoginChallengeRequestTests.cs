using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1735 — POST /api/v1/auth/challenge (ADR 0142 D2). Every load-bearing assertion is an anti-enumeration
/// invariant: known, unknown, cooled, over the mail budget and over the code budget answer ONE shape —
/// status, header names, body keys, no cookie — and the only differences go out of band, to the inbox. A
/// load-dropped enqueue answers the same shape by construction (the port returns nothing); its drop line is
/// pinned in LoginChallengeDispatchChannelTests. Runs on the shared host (real Redis and Postgres, the
/// recording mail fake), each test on its own address, because the budgets are per address and the Redis
/// container is shared across the collection.
/// </summary>
[Collection("Api")]
public class LoginChallengeRequestTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"lc-{label}-{Guid.NewGuid():N}@example.se";

    private Task<HttpResponseMessage> RequestAsync(string? email) =>
        _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);

    private Task<string> CreateAccountAsync(string email) => AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);

    private async Task SpendAsync(RateBudgetScope scope, string email, int times)
    {
        // The production actor that spends a budget, called as the request path calls it.
        var budget = _factory.Services.GetRequiredService<IRateBudget>();
        for (var i = 0; i < times; i++)
            await budget.TryConsumeAsync(scope, email, Ct);
    }

    private List<RecordedLoginChallenge> MailsTo(string email) =>
        _factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    private async Task<LoginChallengeEmail> AwaitMailAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (MailsTo(email).Count == 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        return MailsTo(email).Single().Content;
    }

    /// <summary>
    /// Blocks until the consumer has handled everything enqueued before this call: the channel is FIFO with
    /// one reader, so a sentinel's mail proves the subject was handled, and "no mail" becomes a verdict.
    /// </summary>
    private async Task DrainAsync()
    {
        var sentinel = NewAddress("drain");
        await CreateAccountAsync(sentinel);
        (await RequestAsync(sentinel)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await AwaitMailAsync(sentinel);
    }

    private static async Task<(HttpStatusCode Status, string[] HeaderNames, string[] BodyKeys, bool SetsCookie)>
        ShapeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return (
            response.StatusCode,
            response.Headers.Concat(response.Content.Headers).Select(h => h.Key)
                .Where(name => name is not "Date").Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray(),
            response.Headers.Contains("Set-Cookie"));
    }

    private static string ChallengeIdOf(JsonDocument body) => body.RootElement.GetProperty("challengeId").GetString()!;

    [Fact]
    public async Task Known_unknown_cooled_and_over_budget_addresses_all_answer_one_202_shape()
    {
        var known = NewAddress("known");
        await CreateAccountAsync(known);
        var cooled = NewAddress("cooled");
        await CreateAccountAsync(cooled);
        var overMails = NewAddress("over-mails");
        await CreateAccountAsync(overMails);
        await SpendAsync(LoginChallengePolicy.MailBudget, overMails, 3);
        var overCodes = NewAddress("over-codes");
        await CreateAccountAsync(overCodes);
        await SpendAsync(LoginChallengePolicy.CodeBudget, overCodes, 10);

        await RequestAsync(cooled);

        var shapes = new[]
        {
            await ShapeAsync(await RequestAsync(known)),
            await ShapeAsync(await RequestAsync(NewAddress("unknown"))),
            await ShapeAsync(await RequestAsync(cooled)),
            await ShapeAsync(await RequestAsync(overMails)),
            await ShapeAsync(await RequestAsync(overCodes)),
        };

        shapes[0].Status.ShouldBe(HttpStatusCode.Accepted);
        shapes[0].BodyKeys.ShouldBe(["challengeId"]);
        shapes[0].SetsCookie.ShouldBeFalse();
        foreach (var shape in shapes[1..])
        {
            shape.Status.ShouldBe(shapes[0].Status);
            shape.HeaderNames.ShouldBe(shapes[0].HeaderNames);
            shape.BodyKeys.ShouldBe(shapes[0].BodyKeys);
            shape.SetsCookie.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task A_known_and_an_unknown_address_both_get_a_record_and_one_mail_each()
    {
        var known = NewAddress("rec-known");
        await CreateAccountAsync(known);
        var unknown = NewAddress("rec-unknown");

        using var knownBody = JsonDocument.Parse(await (await RequestAsync(known)).Content.ReadAsStringAsync(Ct));
        using var unknownBody = JsonDocument.Parse(await (await RequestAsync(unknown)).Content.ReadAsStringAsync(Ct));

        // This host has registration open (ApiFactory), so the unknown address is mailed the code that leads
        // to an account (#1737); the closed host's mail is the next test.
        var knownMail = (await AwaitMailAsync(known)).ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        var unknownMail = (await AwaitMailAsync(unknown)).ShouldBeOfType<LoginChallengeEmail.NewAccountCode>();

        // A record exists for both, so a wrong code answers Wrong for both: "never existed" and "burned" can
        // never be told apart by whether the address has an account.
        var store = _factory.Services.GetRequiredService<ILoginChallengeStore>();
        (await store.ConsumeCodeAsync(ChallengeId.FromRaw(ChallengeIdOf(knownBody)), WrongCodeFor(knownMail.Code), Ct))
            .Outcome.ShouldBe(ChallengeOutcome.Wrong);
        (await store.ConsumeCodeAsync(ChallengeId.FromRaw(ChallengeIdOf(unknownBody)), WrongCodeFor(unknownMail.Code), Ct))
            .Outcome.ShouldBe(ChallengeOutcome.Wrong);
    }

    [Fact]
    public async Task Another_spelling_of_an_accounts_address_is_mailed_to_the_accounts_own_spelling()
    {
        // U+017F (ſ) upper-cases to S, so Identity's lookup finds the account stored under the plain s. ONE
        // request: the two spellings share a fingerprint, and so a cooldown.
        var id = Guid.NewGuid().ToString("N");
        var stored = $"lc-s-{id}@example.se";
        var folded = $"lc-ſ-{id}@example.se";
        await CreateAccountAsync(stored);

        (await RequestAsync(folded)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await AwaitMailAsync(stored)).ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        MailsTo(folded).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_registration_closed_an_unknown_address_gets_the_closed_mail_and_a_record_with_no_code()
    {
        var closed = _factory.CreateRegistrationsClosedClient();
        var unknown = NewAddress("closed-unknown");

        var response = await closed.PostAsJsonAsync("/api/v1/auth/challenge", new { email = unknown }, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        (await AwaitMailAsync(unknown)).ShouldBeOfType<LoginChallengeEmail.RegistrationClosed>();
        MailsTo(unknown).Count.ShouldBe(1);

        // No code was minted, so no six digits can verify. Read through the CLOSED host's own store: each host
        // has its own keyring, and another host's store would answer Missing for a payload it cannot open.
        var store = _factory.GetRegistrationsClosedHost().Services.GetRequiredService<ILoginChallengeStore>();
        (await store.ConsumeCodeAsync(ChallengeId.FromRaw(ChallengeIdOf(body)), LoginCode.FromRaw("000000"), Ct))
            .Outcome.ShouldBe(ChallengeOutcome.Wrong);
    }

    [Fact]
    public async Task With_registration_open_a_new_address_past_its_code_budget_gets_the_limit_mail_and_no_code()
    {
        var unknown = NewAddress("limit");
        await SpendAsync(LoginChallengePolicy.CodeBudget, unknown, 10);

        (await RequestAsync(unknown)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await AwaitMailAsync(unknown)).ShouldBeOfType<LoginChallengeEmail.NewAccountCodeLimitReached>();
    }

    private static LoginCode WrongCodeFor(LoginCode code) =>
        LoginCode.FromRaw(code.Reveal() == "000000" ? "111111" : "000000");

    [Fact]
    public async Task A_cooled_repeat_sends_nothing_more()
    {
        var email = NewAddress("cooled-mail");
        await CreateAccountAsync(email);

        await RequestAsync(email);
        await AwaitMailAsync(email);
        await RequestAsync(email);
        await DrainAsync();

        MailsTo(email).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_account_past_its_code_budget_gets_a_link_only_mail()
    {
        var email = NewAddress("link-only");
        await CreateAccountAsync(email);
        await SpendAsync(LoginChallengePolicy.CodeBudget, email, 10);

        await RequestAsync(email);

        (await AwaitMailAsync(email)).ShouldBeOfType<LoginChallengeEmail.LinkOnly>();
    }

    [Fact]
    public async Task A_malformed_address_is_a_400_whatever_it_is()
    {
        (await RequestAsync("not-an-address")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RequestAsync(null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_answers_the_same_503_for_a_known_and_an_unknown_address()
    {
        var known = NewAddress("503-known");
        await CreateAccountAsync(known);

        using var _ = _factory.Emails.Incapable();
        var forKnown = await RequestAsync(known);
        var forUnknown = await RequestAsync(NewAddress("503-unknown"));

        forKnown.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        forUnknown.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await forKnown.Content.ReadAsStringAsync(Ct)).ShouldBe(await forUnknown.Content.ReadAsStringAsync(Ct));
        (await forKnown.Content.ReadAsStringAsync(Ct)).ShouldContain("Auth.EmailDeliveryUnavailable");
    }

    [Fact]
    public async Task An_unreachable_redis_answers_one_uniform_503()
    {
        using var _ = _factory.LoginChallengeFaults.Unavailable();

        var response = await RequestAsync(NewAddress("redis-down"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("error").GetString().ShouldBe(StoreUnavailableException.ClientMessage);
    }
}
