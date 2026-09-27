using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1745 (ADR 0142 Amendment (18)) — a GitHub login end to end, against real Postgres and Redis. GitHub's verified
/// primary address takes Google's path in one click: an account it names is linked and signed in, and a new one waits
/// only for the terms. That no code is ever sent on this path is pinned structurally by
/// <c>LoginProofChainTests.A_provider_login_can_reach_no_login_code</c>.
/// <para>
/// Every premise is minted by production: the flow by the start route, and the GitHub identity by the real
/// <see cref="GitHubIdentityProvider"/> over <see cref="ScriptedGitHub"/> (a documented <c>/user</c> and
/// <c>/user/emails</c> shape).
/// </para>
/// </summary>
[Collection("Api")]
public sealed class GitHubLoginTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // The start budget is one key for the whole host, and this collection shares the host: each row starts from a
    // full budget and leaves one behind.
    public ValueTask InitializeAsync() => ResetStartBudgetAsync();

    public ValueTask DisposeAsync() => ResetStartBudgetAsync();

    private async ValueTask ResetStartBudgetAsync()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        await admin.GetDatabase().KeyDeleteAsync(
            RedisRateBudget.Key(ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress(string label) => $"gh-{label}-{Guid.NewGuid():N}@firma.example";

    // A positive integer below 2^53, as GitHub's ids are; never a hex string, which the adapter would refuse.
    private static long NewGitHubId() => Random.Shared.NextInt64(1_000_000, 1L << 53);

    private static string User(long id) => GitHubApiShapes.User(id, $"user-{id}");

    private sealed record StartedFlow(string State, string Challenge, string RedirectUri);

    private async Task<StartedFlow> StartAsync(string next = "/ansokningar/abc-123", HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/github/start", new { next }, Ct);
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
    private async Task<JsonElement> GitHubLoginAsync(
        long id, string primary, string? emailsJson = null, HttpClient? client = null)
    {
        var flow = await StartAsync(client: client);
        var response = await CallbackAsync(
            GitHubAuthorises(flow, id, emailsJson ?? GitHubApiShapes.Emails.PrimaryVerified(primary)), flow.State, client);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string SignedInSessionOf(JsonElement body)
    {
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

    // ── an existing account ──

    [Fact]
    public async Task A_first_github_login_of_an_existing_account_links_it_and_signs_in()
    {
        var address = NewAddress("befintlig");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        var revokedBefore = factory.GitHub.Revoked.Count;

        var sessionId = SignedInSessionOf(await GitHubLoginAsync(id, address));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(
            new LoginRow("github", id.ToString(CultureInfo.InvariantCulture), null));
        // The column is jsonb, which re-serialises; the row names the provider and nothing else, never the id.
        var payload = JsonDocument.Parse((await LinkedAuditPayloadsAsync(userId)).ShouldHaveSingleItem()!).RootElement;
        payload.EnumerateObject().Select(p => p.Name).ShouldBe(["provider"]);
        payload.GetProperty("provider").GetString().ShouldBe("github");
        // sa m-2: GitHub's token was revoked once the reads were done.
        factory.GitHub.Revoked.Count.ShouldBe(revokedBefore + 1);
    }

    [Fact]
    public async Task The_second_github_login_finds_the_link_and_writes_no_other()
    {
        var address = NewAddress("andra");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();
        SignedInSessionOf(await GitHubLoginAsync(id, address));

        var sessionId = SignedInSessionOf(await GitHubLoginAsync(id, address));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).Count.ShouldBe(1);
        (await LinkedAuditPayloadsAsync(userId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_linked_login_whose_primary_moved_to_another_accounts_address_is_refused()
    {
        // Actor: the GitHub user changing the primary at GitHub. Refused as Google's rename is, and the identifier is
        // never moved.
        var a = NewAddress("a");
        var b = NewAddress("b");
        var userA = await AccountAsync(a);
        var userB = await AccountAsync(b);
        var id = NewGitHubId();
        SignedInSessionOf(await GitHubLoginAsync(id, a));

        var body = await GitHubLoginAsync(id, b);

        body.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await LoginRowsAsync(userA)).Count.ShouldBe(1);
        (await LoginRowsAsync(userB)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_login_binds_the_primary_and_never_the_noreply_address()
    {
        // The noreply entry is reported in practice first in the list (Automattic/gravatar #117).
        var address = NewAddress("noreply");
        var userId = await AccountAsync(address);
        var id = NewGitHubId();

        var sessionId = SignedInSessionOf(await GitHubLoginAsync(
            id, address, GitHubApiShapes.Emails.PrimaryVerifiedWithNoreply(address, id, $"user-{id}")));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().LoginProvider.ShouldBe("github");
        (await UserIdOfAsync(GitHubApiShapes.NoReplyAddress(id, $"user-{id}"))).ShouldBeNull();
    }

    // ── a new address ──

    [Fact]
    public async Task A_new_address_waits_for_the_terms_and_then_gets_an_account_a_link_and_a_session()
    {
        var address = NewAddress("ny");
        var id = NewGitHubId();

        var body = await GitHubLoginAsync(id, address);

        body.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["grantToken", "next", "outcome"]);
        (await UserIdOfAsync(address)).ShouldBeNull("no row is written before the terms are accepted (ADR 0142 D3)");

        var completed = await _client.PostAsJsonAsync(
            "/api/v1/auth/challenge/complete",
            new { grantToken = body.GetProperty("grantToken").GetString(), acceptTerms = true },
            Ct);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sessionId = SignedInSessionOf(await completed.Content.ReadFromJsonAsync<JsonElement>(Ct));

        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(
            new LoginRow("github", id.ToString(CultureInfo.InvariantCulture), null));
        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_registration_closed_a_new_address_is_closed_and_gets_no_grant()
    {
        var address = NewAddress("stangd");

        var body = await GitHubLoginAsync(NewGitHubId(), address, client: factory.CreateRegistrationsClosedClient());

        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
        body.TryGetProperty("grantToken", out _).ShouldBeFalse();
        (await UserIdOfAsync(address)).ShouldBeNull();
    }

    // ── GitHub's own refusals ──

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
        var address = NewAddress("spent");
        var ownCode = GitHubAuthorises(flow, NewGitHubId(), GitHubApiShapes.Emails.PrimaryVerified(address));
        (await CallbackAsync(ownCode, flow.State)).StatusCode.ShouldBe(HttpStatusCode.Gone);
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
        (await UserIdOfAsync(address)).ShouldBeNull();
    }

    // ── both providers ──

    [Fact]
    public async Task One_account_logs_in_with_google_and_with_github_and_holds_both_links()
    {
        // P12: kills a lookup or a store that ignores the provider.
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
        var google = await _client.PostAsJsonAsync(
            "/api/v1/auth/oauth/google/callback",
            new { code = googleCode, state = googleBody.GetProperty("state").GetString() },
            Ct);
        SignedInSessionOf(await google.Content.ReadFromJsonAsync<JsonElement>(Ct));

        SignedInSessionOf(await GitHubLoginAsync(NewGitHubId(), address));

        (await LoginRowsAsync(userId)).Select(r => r.LoginProvider).ShouldBe(["github", "google"], ignoreOrder: true);
        (await LinkedAuditPayloadsAsync(userId))
            .Select(p => JsonDocument.Parse(p!).RootElement.GetProperty("provider").GetString())
            .ShouldBe(["github", "google"], ignoreOrder: true);
    }
}
