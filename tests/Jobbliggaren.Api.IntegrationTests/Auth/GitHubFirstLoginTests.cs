using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1745 (ADR 0142 Amendment (16); senior-cto-advisor 1a, "the first link needs a code") — a GitHub login end to end,
/// against real Postgres and Redis. GitHub's address is only asserted: the first time, it chooses where a login code
/// goes, and only that code, typed into the browser that ran the GitHub flow, binds the login. Rows are numbered as in
/// the 6b form reading (test-writer §3).
/// <para>
/// Every premise is minted by production: the flow by the start route, the GitHub identity by the real
/// <see cref="GitHubIdentityProvider"/> over <see cref="ScriptedGitHub"/> (a documented <c>/user</c> and
/// <c>/user/emails</c> shape), and every code and link is read out of the mail the dispatch consumer sent
/// (the <c>LoginChallengeProofTests</c> pattern). The one premise production does not yet produce is GitHub being
/// registered on a host: in 6b PR 1 <see cref="ApiFactory"/> builds it by hand, and its actor is
/// <c>AddGitHubIdentityProvider</c>, the gate 6b PR 2 adds.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class GitHubFirstLoginTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Both budgets are one key for the whole host, and this collection shares the host: each row starts from a full
    // budget. The mail budget for addresses without an account allows 20 a day, host-wide.
    public ValueTask InitializeAsync() => ResetHostWideBudgetsAsync();

    public ValueTask DisposeAsync() => ResetHostWideBudgetsAsync();

    private async ValueTask ResetHostWideBudgetsAsync()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        await admin.GetDatabase().KeyDeleteAsync(
        [
            RedisRateBudget.Key(ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject),
            RedisRateBudget.Key(
                LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject),
        ]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"gh-{label}-{Guid.NewGuid():N}@firma.example";

    // A positive integer below 2^53, as GitHub's ids are; never a hex string, which the adapter would refuse.
    private static long NewGitHubId() => Random.Shared.NextInt64(1_000_000, 1L << 53);

    private static string User(long id) => GitHubApiShapes.User(id, $"user-{id}");

    private sealed record StartedFlow(string State, string Challenge, string RedirectUri);

    private async Task<StartedFlow> StartAsync(string? next = "/ansokningar/abc-123")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/oauth/github/start", new { next }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var query = HttpUtility.ParseQueryString(new Uri(body.GetProperty("authorizeUrl").GetString()!).Query);
        return new StartedFlow(body.GetProperty("state").GetString()!, query["code_challenge"]!, query["redirect_uri"]!);
    }

    // What GitHub does after the user approves the app: a code bound to this flow's challenge and redirect URI, whose
    // token reads the given /user document and /user/emails list.
    private string GitHubAuthorises(StartedFlow flow, long id, string emailsJson)
    {
        var code = $"scripted-{Guid.NewGuid():N}";
        factory.GitHub.Expect(code, User(id), emailsJson, flow.Challenge, flow.RedirectUri);
        return code;
    }

    private Task<HttpResponseMessage> CallbackAsync(string code, string state, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/github/callback", new { code, state }, Ct);

    /// <summary>A whole GitHub login up to the callback's answer: start, approve, return.</summary>
    private async Task<HttpResponseMessage> GitHubLoginAsync(
        long id, string primary, string? emailsJson = null, string? next = "/ansokningar/abc-123")
    {
        var flow = await StartAsync(next);
        return await CallbackAsync(
            GitHubAuthorises(flow, id, emailsJson ?? GitHubApiShapes.Emails.PrimaryVerified(primary)), flow.State);
    }

    private static async Task<JsonElement> OkBodyAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private sealed record CodeStep(string ChallengeId, string LinkGrant, string Email);

    private static async Task<CodeStep> CodeStepOfAsync(HttpResponseMessage response)
    {
        var body = await OkBodyAsync(response);
        body.GetProperty("outcome").GetString().ShouldBe(ExternalLoginCompletion.CodeRequired.WireName);
        return new CodeStep(
            body.GetProperty("challengeId").GetString()!,
            body.GetProperty("linkGrant").GetString()!,
            body.GetProperty("email").GetString()!);
    }

    private List<RecordedLoginChallenge> MailsTo(string email) =>
        factory.Emails.LoginChallenges.Where(m => m.ToEmail == email).ToList();

    private async Task<LoginChallengeEmail> NextMailToAsync(string email, int before)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (MailsTo(email).Count == before)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        return MailsTo(email)[^1].Content;
    }

    private static string CodeIn(LoginChallengeEmail mail) => mail switch
    {
        LoginChallengeEmail.CodeAndLink both => both.Code.Reveal(),
        LoginChallengeEmail.NewAccountCode code => code.Code.Reveal(),
        _ => throw new InvalidOperationException($"A {mail.GetType().Name} mail carries no code."),
    };

    private Task<HttpResponseMessage> VerifyAsync(
        string challengeId, string code, string? linkGrant = null, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync(
            "/api/v1/auth/challenge/verify", new { challengeId, code, linkGrant }, Ct);

    private Task<HttpResponseMessage> CompleteAsync(string grantToken) =>
        _client.PostAsJsonAsync("/api/v1/auth/challenge/complete", new { grantToken, acceptTerms = true }, Ct);

    private static async Task<string> SignedInSessionOfAsync(HttpResponseMessage response)
    {
        var body = await OkBodyAsync(response);
        body.GetProperty("outcome").GetString().ShouldBe("signedIn");
        return body.GetProperty("sessionId").GetString()!;
    }

    private async Task<Guid?> UserIdOfAsync(string address)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = address.ToUpperInvariant();
        return (await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, Ct))?.Id;
    }

    private async Task<Guid> AccountAsync(string address)
    {
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, address, ct: Ct);
        return (await UserIdOfAsync(address)).ShouldNotBeNull();
    }

    private sealed record LoginRow(string LoginProvider, string ProviderKey, string? ProviderDisplayName);

    private async Task<List<LoginRow>> LoginRowsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins.AsNoTracking()
            .Where(l => l.UserId == userId)
            .Select(l => new LoginRow(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))
            .ToListAsync(Ct);
    }

    private async Task<List<string?>> LinkedAuditPayloadsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(e => e.UserId == userId && e.EventType == ExternalLoginLinker.ExternalLoginLinkedAuditEventType)
            .Select(e => e.Payload)
            .ToListAsync(Ct);
    }

    private async Task<HttpStatusCode> MeAsync(string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await _client.SendAsync(request, Ct)).StatusCode;
    }

    private static async Task<string> ComparableAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (int)response.StatusCode + "|" + string.Join(
            "|", json.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
    }

    /// <summary>A first GitHub login of an account that already exists, bound by the mailed code. Row 5's steps.</summary>
    private async Task<string> BindWithTheMailedCodeAsync(long id, string address)
    {
        var before = MailsTo(address).Count;
        var step = await CodeStepOfAsync(await GitHubLoginAsync(id, address));
        var mail = await NextMailToAsync(address, before);
        return await SignedInSessionOfAsync(await VerifyAsync(step.ChallengeId, CodeIn(mail), step.LinkGrant));
    }

    // ── the callback without a link ──

    [Fact]
    public async Task Row1_The_first_callback_answers_the_code_step_alike_whether_or_not_the_address_has_an_account()
    {
        var holder = NewAddress("har-konto");
        await AccountAsync(holder);
        var stranger = NewAddress("utan-konto");

        var withAccount = await OkBodyAsync(await GitHubLoginAsync(NewGitHubId(), holder));
        var withoutAccount = await OkBodyAsync(await GitHubLoginAsync(NewGitHubId(), stranger));

        foreach (var body in new[] { withAccount, withoutAccount })
        {
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                .ShouldBe(["challengeId", "email", "linkGrant", "next", "outcome"]);
            body.GetProperty("outcome").GetString().ShouldBe("codeRequired");
            body.GetProperty("next").GetString().ShouldBe("/ansokningar/abc-123");
        }

        // The address is an echo of GitHub's primary, never input the server reads back.
        withAccount.GetProperty("email").GetString().ShouldBe(holder);
        withoutAccount.GetProperty("email").GetString().ShouldBe(stranger);
    }

    [Fact]
    public async Task The_code_step_leaves_out_the_path_when_the_flow_carried_none()
    {
        var body = await OkBodyAsync(await GitHubLoginAsync(NewGitHubId(), NewAddress("utan-next"), next: null));

        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["challengeId", "email", "linkGrant", "outcome"]);
    }

    [Fact]
    public async Task Row1c_With_mail_undeliverable_an_unlinked_login_is_a_503_and_a_linked_one_still_signs_in()
    {
        // Minor F: the capability is read after the link lookup, so the answer varies with the configuration and with
        // whether the presented identity is linked, which only its holder can present. The linked login is bound
        // first, while mail is deliverable.
        var address = NewAddress("leverans");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        await BindWithTheMailedCodeAsync(id, address);

        using (factory.Emails.Incapable())
        {
            var unlinked = await GitHubLoginAsync(NewGitHubId(), NewAddress("leverans-ny"));
            unlinked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await unlinked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()
                .ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);

            await SignedInSessionOfAsync(await GitHubLoginAsync(id, address));
        }

        (await LoginRowsAsync(userId)).Count.ShouldBe(1);
    }

    // ── binding by the code ──

    [Fact]
    public async Task Row5_A_first_github_login_of_an_existing_account_binds_with_the_mailed_code_and_signs_in()
    {
        var address = NewAddress("befintlig");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        var revokedBefore = factory.GitHub.Revoked.Count;

        var sessionId = await BindWithTheMailedCodeAsync(id, address);

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(
            new LoginRow("github", id.ToString(CultureInfo.InvariantCulture), null));
        var payload = JsonDocument.Parse((await LinkedAuditPayloadsAsync(userId)).ShouldHaveSingleItem()!).RootElement;
        payload.EnumerateObject().Select(p => p.Name).ShouldBe(["provider"]);
        payload.GetProperty("provider").GetString().ShouldBe("github");
        // sa m-2: GitHub's token was revoked once the reads were done.
        factory.GitHub.Revoked.Count.ShouldBe(revokedBefore + 1);
    }

    [Fact]
    public async Task Row8_The_second_github_login_goes_straight_in_and_mails_nothing()
    {
        var address = NewAddress("andra");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        await BindWithTheMailedCodeAsync(id, address);
        var mailsBefore = MailsTo(address).Count;

        var sessionId = await SignedInSessionOfAsync(await GitHubLoginAsync(id, address));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        MailsTo(address).Count.ShouldBe(mailsBefore);
        (await LoginRowsAsync(userId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Row2_The_link_in_the_mail_signs_in_and_binds_nothing()
    {
        // senior-cto-advisor 1a: the link is consumed wherever it is clicked, and the link page never reads the flow
        // cookie, so it cannot carry the pending link. Actor: the user who clicks the link instead of typing the code.
        var address = NewAddress("lank");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        var before = MailsTo(address).Count;
        await CodeStepOfAsync(await GitHubLoginAsync(id, address));
        var mail = (await NextMailToAsync(address, before)).ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();

        await SignedInSessionOfAsync(
            await _client.PostAsJsonAsync("/api/v1/auth/link", new { token = mail.Link.Reveal() }, Ct));

        (await LoginRowsAsync(userId)).ShouldBeEmpty();
        await CodeStepOfAsync(await GitHubLoginAsync(id, address));
    }

    [Fact]
    public async Task Row3_A_wrong_code_leaves_the_pending_link_for_the_right_one()
    {
        // Actor: a typo, the store's Wrong verdict (LoginChallengeProofTests). Kills "redeem before the code verified".
        var address = NewAddress("fel-kod");
        var userId = await AccountAsync(address);
        var before = MailsTo(address).Count;
        var step = await CodeStepOfAsync(await GitHubLoginAsync(NewGitHubId(), address));
        var code = CodeIn(await NextMailToAsync(address, before));
        var wrong = code == "000000" ? "111111" : "000000";

        (await VerifyAsync(step.ChallengeId, wrong, step.LinkGrant)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await SignedInSessionOfAsync(await VerifyAsync(step.ChallengeId, code, step.LinkGrant));

        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().LoginProvider.ShouldBe("github");
    }

    [Fact]
    public async Task Row4_A_pending_link_for_one_address_and_a_code_for_another_signs_the_code_in_and_binds_nothing()
    {
        // security-auditor V-1: the grant's server-held address is compared with the address the code proved, never
        // the cookie's. Actor: a client posting to verify its own pending link beside its own challenge for another
        // address; the Api takes both from the request body. Both addresses hold accounts, so a binding has a target.
        var x = NewAddress("x");
        var z = NewAddress("z");
        var userX = await AccountAsync(x);
        var userZ = await AccountAsync(z);
        var pending = await CodeStepOfAsync(await GitHubLoginAsync(NewGitHubId(), x));

        var before = MailsTo(z).Count;
        var requested = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email = z }, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var challengeZ = (await requested.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("challengeId").GetString()!;
        var codeZ = CodeIn(await NextMailToAsync(z, before));

        var sessionId = await SignedInSessionOfAsync(await VerifyAsync(challengeZ, codeZ, pending.LinkGrant));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userX)).ShouldBeEmpty();
        (await LoginRowsAsync(userZ)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Row6_A_new_address_is_proven_by_the_code_and_gets_its_account_and_the_link_on_the_terms()
    {
        var address = NewAddress("ny");
        var id = NewGitHubId();
        var before = MailsTo(address).Count;
        var step = await CodeStepOfAsync(await GitHubLoginAsync(id, address));
        var mail = (await NextMailToAsync(address, before)).ShouldBeOfType<LoginChallengeEmail.NewAccountCode>();
        (await UserIdOfAsync(address)).ShouldBeNull("nothing is written before the terms are accepted (ADR 0142 D3)");

        var consent = await OkBodyAsync(await VerifyAsync(step.ChallengeId, mail.Code.Reveal(), step.LinkGrant));
        consent.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        var sessionId = await SignedInSessionOfAsync(await CompleteAsync(consent.GetProperty("grantToken").GetString()!));

        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(
            new LoginRow("github", id.ToString(CultureInfo.InvariantCulture), null));
        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Row7_With_registration_closed_at_the_proof_the_code_bound_login_is_closed_and_links_nothing()
    {
        // Actor: the operator's kill-switch thrown between the mint and the proof. With it closed at the mint no code
        // is mailed at all (LoginChallengePlan), so this state is reached only by the switch. The closed host shares
        // this host's Redis and Postgres.
        var address = NewAddress("stangd");
        var before = MailsTo(address).Count;
        var step = await CodeStepOfAsync(await GitHubLoginAsync(NewGitHubId(), address));
        var mail = (await NextMailToAsync(address, before)).ShouldBeOfType<LoginChallengeEmail.NewAccountCode>();

        var body = await OkBodyAsync(await VerifyAsync(
            step.ChallengeId, mail.Code.Reveal(), step.LinkGrant, factory.CreateRegistrationsClosedClient()));

        body.EnumerateObject().Select(p => p.Name).ShouldBe(["outcome"]);
        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
        (await UserIdOfAsync(address)).ShouldBeNull();
    }

    [Fact]
    public async Task Row10_The_pending_link_grant_is_never_a_registration_grant()
    {
        // Major A: the grant from the code step, posted by its holder to complete with the terms accepted. Its address
        // is only asserted, so an account opened on it would be the hijack M-1 closes.
        var address = NewAddress("kapning");
        var step = await CodeStepOfAsync(await GitHubLoginAsync(NewGitHubId(), address));

        var completed = await CompleteAsync(step.LinkGrant);

        completed.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await completed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()
            .ShouldBe(AuthErrorCodes.LoginGrantUnusable);
        (await UserIdOfAsync(address)).ShouldBeNull();
    }

    [Fact]
    public async Task Row14_An_expired_pending_link_leaves_the_codes_own_login()
    {
        // Actor: Redis's TTL, run out here by hand as the flow rows do; the grant lives 10 minutes, the code 15.
        var address = NewAddress("utgangen");
        var userId = await AccountAsync(address);
        var before = MailsTo(address).Count;
        var step = await CodeStepOfAsync(await GitHubLoginAsync(NewGitHubId(), address));
        var code = CodeIn(await NextMailToAsync(address, before));
        await using (var admin = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString))
        {
            (await admin.GetDatabase().KeyExpireAsync(
                RedisGrantStore.Key(GrantToken.FromRaw(step.LinkGrant)), TimeSpan.FromMilliseconds(1))).ShouldBeTrue();
        }

        await Task.Delay(50, Ct);

        await SignedInSessionOfAsync(await VerifyAsync(step.ChallengeId, code, step.LinkGrant));
        (await LoginRowsAsync(userId)).ShouldBeEmpty();
    }

    // ── the found link ──

    [Fact]
    public async Task Row15_A_linked_login_whose_primary_moved_to_another_accounts_address_is_refused_on_the_found_branch()
    {
        // Actor: the GitHub user changing the primary at GitHub. The found branch refuses as Google's does and never
        // falls through to the code step: the answer is the refusal, not codeRequired.
        var a = NewAddress("a");
        var b = NewAddress("b");
        var userA = await AccountAsync(a);
        await AccountAsync(b);
        var id = NewGitHubId();
        await BindWithTheMailedCodeAsync(id, a);

        var body = await OkBodyAsync(await GitHubLoginAsync(id, b));

        body.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await LoginRowsAsync(userA)).Count.ShouldBe(1);
    }

    // ── GitHub's own shapes ──

    [Fact]
    public async Task Row16_The_code_goes_to_the_primary_and_never_to_the_noreply_address()
    {
        // The noreply entry is reported in practice first in the list (Automattic/gravatar #117).
        var address = NewAddress("noreply");
        await AccountAsync(address);
        var id = NewGitHubId();
        var noreply = GitHubApiShapes.NoReplyAddress(id, $"user-{id}");
        var before = MailsTo(address).Count;

        var step = await CodeStepOfAsync(await GitHubLoginAsync(
            id, address, GitHubApiShapes.Emails.PrimaryVerifiedWithNoreply(address, id, $"user-{id}")));

        step.Email.ShouldBe(address);
        await NextMailToAsync(address, before);
        MailsTo(noreply).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_code_github_refuses_is_the_unusable_answer_and_spends_the_flow()
    {
        // P9: GitHub answers a refused code with 200 and bad_verification_code.
        var flow = await StartAsync();

        var refused = await CallbackAsync("scripted-never-issued", flow.State);
        var unknown = await CallbackAsync("scripted-never-issued", OAuthState.Generate().Reveal());

        var answer = await ComparableAsync(refused);
        answer.ShouldBe(await ComparableAsync(unknown));
        answer.ShouldStartWith("410|");
        (await CallbackAsync(GitHubAuthorises(flow, NewGitHubId(), GitHubApiShapes.Emails.PrimaryVerified(NewAddress("spent"))), flow.State))
            .StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task An_unverified_primary_at_github_is_refused_with_the_actionable_code_and_reads_nothing_else()
    {
        // P10 (test-writer Major 1, senior-cto-advisor point 2): GitHub issues no token for a user whose primary is
        // unverified, and answers unverified_user_email; the refusal is the address rule's, 400.
        var address = NewAddress("overifierad");
        var flow = await StartAsync();
        var code = GitHubAuthorises(flow, NewGitHubId(), GitHubApiShapes.Emails.PrimaryVerified(address));
        var requestsBefore = factory.GitHub.Requests.Count;

        HttpResponseMessage response;
        factory.GitHub.TokenError = "unverified_user_email";
        try
        {
            response = await CallbackAsync(code, flow.State);
        }
        finally
        {
            factory.GitHub.TokenError = null;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ComparableAsync(response)).ShouldContain(AuthErrorCodes.ExternalEmailUnverified);
        factory.GitHub.Requests.Skip(requestsBefore).ShouldHaveSingleItem().Uri.AbsoluteUri
            .ShouldBe(ScriptedGitHub.TokenEndpoint);
    }

    [Fact]
    public async Task One_account_logs_in_with_google_and_with_github_and_holds_both_links()
    {
        // P12: kills a lookup or a store that ignores the provider. Google binds on its own authority; GitHub by code.
        var address = NewAddress("bada");
        var userId = await AccountAsync(address);
        var googleFlow = await _client.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next = "/oversikt" }, Ct);
        var googleBody = await googleFlow.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var googleQuery = HttpUtility.ParseQueryString(new Uri(googleBody.GetProperty("authorizeUrl").GetString()!).Query);
        var googleCode = $"4/0AVGzR1{Guid.NewGuid():N}";
        factory.Google.Expect(
            googleCode,
            GoogleUserInfoShapes.Workspace(Guid.NewGuid().ToString("N"), address, hostedDomain: "firma.example"),
            googleQuery["code_challenge"],
            googleQuery["redirect_uri"]);
        await SignedInSessionOfAsync(await _client.PostAsJsonAsync(
            "/api/v1/auth/oauth/google/callback",
            new { code = googleCode, state = googleBody.GetProperty("state").GetString() },
            Ct));

        await BindWithTheMailedCodeAsync(NewGitHubId(), address);

        (await LoginRowsAsync(userId)).Select(r => r.LoginProvider).ShouldBe(["github", "google"], ignoreOrder: true);
        (await LinkedAuditPayloadsAsync(userId))
            .Select(p => JsonDocument.Parse(p!).RootElement.GetProperty("provider").GetString())
            .ShouldBe(["github", "google"], ignoreOrder: true);
    }
}
