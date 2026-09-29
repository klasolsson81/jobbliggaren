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
/// #1746 (ADR 0142 D8) — a LinkedIn login end to end, against real Postgres and Redis. LinkedIn's verified primary
/// address takes Google's path in one click: an account it names is linked and signed in, and a new one waits only for
/// the terms. That no code is ever sent on this path is pinned structurally by
/// <c>LoginProofChainTests.A_provider_login_can_reach_no_login_code</c>.
/// <para>
/// Every premise is minted by production: the flow by the start route, and the LinkedIn identity by the real
/// <see cref="LinkedInIdentityProvider"/> over <see cref="ScriptedLinkedIn"/> (a documented userinfo shape).
/// </para>
/// </summary>
[Collection("Api")]
public sealed class LinkedInLoginTests(ApiFactory factory) : IAsyncLifetime
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

    private static string NewAddress(string label) => $"li-{label}-{Guid.NewGuid():N}@firma.example";

    private sealed record StartedFlow(string State, string RedirectUri);

    private async Task<StartedFlow> StartAsync(string next = "/ansokningar/abc-123", HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/linkedin/start", new { next }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var query = HttpUtility.ParseQueryString(new Uri(body.GetProperty("authorizeUrl").GetString()!).Query);
        return new StartedFlow(body.GetProperty("state").GetString()!, query["redirect_uri"]!);
    }

    // What LinkedIn does after the member consents: a code bound to this flow's redirect URI, whose token reads the
    // given userinfo document.
    private string LinkedInAuthorises(StartedFlow flow, string userInfoJson)
    {
        var code = $"AQT{Guid.NewGuid():N}";
        factory.LinkedIn.Expect(code, userInfoJson, redirectUri: flow.RedirectUri);
        return code;
    }

    private Task<HttpResponseMessage> CallbackAsync(string code, string state, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/linkedin/callback", new { code, state }, Ct);

    /// <summary>A whole LinkedIn login up to the callback's answer: start, consent, return.</summary>
    private async Task<JsonElement> LinkedInLoginAsync(
        string sub, string primary, string? userInfoJson = null, HttpClient? client = null)
    {
        var flow = await StartAsync(client: client);
        var response = await CallbackAsync(
            LinkedInAuthorises(flow, userInfoJson ?? LinkedInUserInfoShapes.Member(sub, primary)), flow.State, client);
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

    private List<string> LinkedInRequestsSince(int before) =>
        [.. factory.LinkedIn.Requests.Skip(before).Select(r => $"{r.Method} {r.Uri.GetLeftPart(UriPartial.Path)}")];

    // ── the start ──

    [Fact]
    public async Task A_linkedin_start_points_at_linkedins_authorize_endpoint_with_the_five_documented_parameters()
    {
        var store = (FaultableOAuthStateStore)factory.Services.GetRequiredService<IOAuthStateStore>();
        var before = store.Writes;

        var response = await _client.PostAsJsonAsync("/api/v1/auth/oauth/linkedin/start", new { next = "/oversikt" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["authorizeUrl", "state"]);
        var url = new Uri(body.GetProperty("authorizeUrl").GetString()!);
        url.GetLeftPart(UriPartial.Path).ShouldBe("https://www.linkedin.com/oauth/v2/authorization");
        var query = HttpUtility.ParseQueryString(url.Query);
        query.AllKeys.Order(StringComparer.Ordinal).ShouldBe(
            ["client_id", "redirect_uri", "response_type", "scope", "state"]);
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe(ApiFactory.LinkedInClientId);
        query["scope"].ShouldBe("openid email");
        new Uri(query["redirect_uri"]!).AbsolutePath.ShouldBe("/api/auth/oauth/linkedin/callback");
        query["state"].ShouldBe(body.GetProperty("state").GetString());
        store.Writes.ShouldBe(before + 1);
    }

    // ── an existing account ──

    [Fact]
    public async Task A_first_linkedin_login_of_an_existing_account_links_it_and_signs_in()
    {
        var address = NewAddress("befintlig");
        var userId = await AccountAsync(address);
        var sub = LinkedInUserInfoShapes.NewSub();
        var requestsBefore = factory.LinkedIn.Requests.Count;

        var sessionId = SignedInSessionOf(await LinkedInLoginAsync(sub, address));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(new LoginRow("linkedin", sub, null));
        // The column is jsonb, which re-serialises; the row names the provider and nothing else, never the subject.
        var payload = JsonDocument.Parse((await LinkedAuditPayloadsAsync(userId)).ShouldHaveSingleItem()!).RootElement;
        payload.EnumerateObject().Select(p => p.Name).ShouldBe(["provider"]);
        payload.GetProperty("provider").GetString().ShouldBe("linkedin");
        // One token request and one userinfo read, nothing else: no introspection and no revocation.
        LinkedInRequestsSince(requestsBefore).ShouldBe(
            [$"POST {ScriptedLinkedIn.TokenEndpoint}", $"GET {ScriptedLinkedIn.UserInfoEndpoint}"]);
    }

    [Fact]
    public async Task The_second_linkedin_login_finds_the_link_and_writes_no_other()
    {
        var address = NewAddress("andra");
        var userId = await AccountAsync(address);
        var sub = LinkedInUserInfoShapes.NewSub();
        SignedInSessionOf(await LinkedInLoginAsync(sub, address));

        var sessionId = SignedInSessionOf(await LinkedInLoginAsync(sub, address));

        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).Count.ShouldBe(1);
        (await LinkedAuditPayloadsAsync(userId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_linked_login_whose_primary_moved_to_another_accounts_address_is_refused()
    {
        // Actor: the member making another verified address primary at LinkedIn (LinkedIn Help a519904, read
        // 2026-09-27). Refused as Google's rename is, and the identifier is never moved.
        var a = NewAddress("a");
        var b = NewAddress("b");
        var userA = await AccountAsync(a);
        var userB = await AccountAsync(b);
        var sub = LinkedInUserInfoShapes.NewSub();
        SignedInSessionOf(await LinkedInLoginAsync(sub, a));

        var body = await LinkedInLoginAsync(sub, b);

        body.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await LoginRowsAsync(userA)).Count.ShouldBe(1);
        (await LoginRowsAsync(userB)).ShouldBeEmpty();
    }

    // ── a new address ──

    [Fact]
    public async Task A_new_address_waits_for_the_terms_and_then_gets_an_account_a_link_and_a_session()
    {
        var address = NewAddress("ny");
        var sub = LinkedInUserInfoShapes.NewSub();

        var body = await LinkedInLoginAsync(sub, address);

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
        (await LoginRowsAsync(userId)).ShouldHaveSingleItem().ShouldBe(new LoginRow("linkedin", sub, null));
        (await MeAsync(sessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_registration_closed_a_new_address_is_closed_and_gets_no_grant()
    {
        var address = NewAddress("stangd");

        var body = await LinkedInLoginAsync(
            LinkedInUserInfoShapes.NewSub(), address, client: factory.CreateRegistrationsClosedClient());

        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
        body.TryGetProperty("grantToken", out _).ShouldBeFalse();
        (await UserIdOfAsync(address)).ShouldBeNull();
    }

    // ── LinkedIn's own refusals ──

    [Fact]
    public async Task A_code_linkedin_refuses_is_the_unusable_answer_and_spends_the_flow()
    {
        // LinkedIn answers a code it cannot find with 401 invalid_request ("3-Legged OAuth Flow").
        var flow = await StartAsync();

        var refused = await CallbackAsync("AQTnever-issued", flow.State);
        var unknown = await CallbackAsync("AQTnever-issued", OAuthState.Generate().Reveal());

        var answer = await ComparableAsync(refused);
        answer.ShouldBe(await ComparableAsync(unknown));
        answer.ShouldStartWith("410|");
        var ownCode = LinkedInAuthorises(flow, LinkedInUserInfoShapes.Member(LinkedInUserInfoShapes.NewSub(), NewAddress("spent")));
        (await CallbackAsync(ownCode, flow.State)).StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    public static TheoryData<string> AddressesLinkedInDoesNotVouchFor => new()
    {
        "unverified", "neither field", "an address without its flag", "a flag without its address",
    };

    [Theory]
    [MemberData(nameof(AddressesLinkedInDoesNotVouchFor))]
    public async Task An_address_linkedin_does_not_vouch_for_is_refused_with_the_actionable_code_and_links_nothing(
        string form)
    {
        // "The 'email' and 'email_verified' fields are optional and may not be included in all responses."
        var address = NewAddress("ointygad");
        var sub = LinkedInUserInfoShapes.NewSub();
        var userInfo = form switch
        {
            "unverified" => LinkedInUserInfoShapes.Unverified(sub, address),
            "neither field" => LinkedInUserInfoShapes.WithoutAddress(sub),
            "an address without its flag" => LinkedInUserInfoShapes.AddressWithoutFlag(sub, address),
            _ => LinkedInUserInfoShapes.FlagWithoutAddress(sub),
        };
        var flow = await StartAsync();
        var requestsBefore = factory.LinkedIn.Requests.Count;

        var response = await CallbackAsync(LinkedInAuthorises(flow, userInfo), flow.State);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ComparableAsync(response)).ShouldContain(AuthErrorCodes.ExternalEmailUnverified);
        (await UserIdOfAsync(address)).ShouldBeNull();
        LinkedInRequestsSince(requestsBefore).ShouldBe(
            [$"POST {ScriptedLinkedIn.TokenEndpoint}", $"GET {ScriptedLinkedIn.UserInfoEndpoint}"]);
    }

    [Fact]
    public async Task The_token_request_carries_no_verifier()
    {
        // LinkedIn's web flow takes no PKCE, and a verifier in a confidential client's request is answered 401
        // invalid_client (declared, executor#2087).
        var address = NewAddress("utan-verifierare");
        await AccountAsync(address);
        var requestsBefore = factory.LinkedIn.Requests.Count;

        SignedInSessionOf(await LinkedInLoginAsync(LinkedInUserInfoShapes.NewSub(), address));

        var token = factory.LinkedIn.Requests.Skip(requestsBefore).First();
        token.Uri.AbsoluteUri.ShouldBe(ScriptedLinkedIn.TokenEndpoint);
        token.Form.Keys.ShouldNotContain("code_verifier");
    }

    // ── three providers ──

    [Fact]
    public async Task One_account_logs_in_with_each_provider_and_holds_three_links()
    {
        // Kills a lookup or a store that ignores the provider.
        var address = NewAddress("alla");
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

        var githubFlow = await _client.PostAsJsonAsync("/api/v1/auth/oauth/github/start", new { next = "/oversikt" }, Ct);
        var githubBody = await githubFlow.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var githubQuery = HttpUtility.ParseQueryString(new Uri(githubBody.GetProperty("authorizeUrl").GetString()!).Query);
        var githubId = Random.Shared.NextInt64(1_000_000, 1L << 53);
        var githubCode = $"scripted-{Guid.NewGuid():N}";
        factory.GitHub.Expect(
            githubCode,
            GitHubApiShapes.User(githubId, $"user-{githubId}"),
            GitHubApiShapes.Emails.PrimaryVerified(address),
            githubQuery["code_challenge"],
            githubQuery["redirect_uri"]);
        var github = await _client.PostAsJsonAsync(
            "/api/v1/auth/oauth/github/callback",
            new { code = githubCode, state = githubBody.GetProperty("state").GetString() },
            Ct);
        SignedInSessionOf(await github.Content.ReadFromJsonAsync<JsonElement>(Ct));

        SignedInSessionOf(await LinkedInLoginAsync(LinkedInUserInfoShapes.NewSub(), address));

        (await LoginRowsAsync(userId)).Select(r => r.LoginProvider)
            .ShouldBe(["github", "google", "linkedin"], ignoreOrder: true);
        (await LinkedAuditPayloadsAsync(userId))
            .Select(p => JsonDocument.Parse(p!).RootElement.GetProperty("provider").GetString())
            .ShouldBe(["github", "google", "linkedin"], ignoreOrder: true);
    }
}
