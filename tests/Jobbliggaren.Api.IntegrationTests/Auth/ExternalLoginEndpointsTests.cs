using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1744 (ADR 0142 D8) — the external login through the Api, against real Postgres and Redis. The production Google
/// adapter runs over <see cref="ScriptedGoogle"/>: the flow is started here, "Google" is handed the code for that
/// flow's challenge and redirect URI, and the callback completes it. Every identity is a documented userinfo shape
/// read by the adapter itself. The #1744 acceptance rows are marked.
/// </summary>
[Collection("Api")]
public sealed class ExternalLoginEndpointsTests(ApiFactory factory) : IAsyncLifetime
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

    private static string NewAddress(string prefix) => $"{prefix}-{Guid.NewGuid():N}@firma.example";

    private static string NewSubject() => Guid.NewGuid().ToString("N");

    private static string Workspace(string sub, string address) =>
        GoogleUserInfoShapes.Workspace(sub, address, hostedDomain: "firma.example");

    private sealed record StartedFlow(string State, string Challenge, string RedirectUri);

    private async Task<StartedFlow> StartAsync(string next = "/ansokningar/abc-123", HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var state = body.GetProperty("state").GetString()!;
        var query = HttpUtility.ParseQueryString(new Uri(body.GetProperty("authorizeUrl").GetString()!).Query);
        query["state"].ShouldBe(state);
        return new StartedFlow(state, query["code_challenge"]!, query["redirect_uri"]!);
    }

    // What Google does after the user consents: it hands the browser a code bound to this flow's challenge and
    // redirect URI, and its userinfo will answer the given document.
    private string GoogleAuthorises(StartedFlow flow, string userInfoJson)
    {
        var code = $"4/0AVGzR1{Guid.NewGuid():N}";
        factory.Google.Expect(code, userInfoJson, flow.Challenge, flow.RedirectUri);
        return code;
    }

    private Task<HttpResponseMessage> CallbackAsync(string code, string state, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync("/api/v1/auth/oauth/google/callback", new { code, state }, Ct);

    private async Task<JsonElement> SignInByGoogleAsync(string sub, string address, HttpClient? client = null)
    {
        var flow = await StartAsync(client: client);
        var response = await CallbackAsync(GoogleAuthorises(flow, Workspace(sub, address)), flow.State, client);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<string> ComparableAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (int)response.StatusCode + "|" + string.Join(
            "|", json.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
    }

    private async Task<Guid?> UserIdOfAsync(string address)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var normalized = address.ToUpperInvariant();
        return (await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, Ct))?.Id;
    }

    private async Task<int> LoginRowsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .CountAsync(l => l.UserId == userId, Ct);
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

    // ── the providers list ──

    [Fact]
    public async Task The_providers_list_names_google_and_is_publicly_cacheable_for_five_minutes()
    {
        // Acceptance: ["google"] when configured; the [] half is GoogleIdentityProviderGateTests'.
        var response = await _client.GetAsync("/api/v1/auth/oauth/providers", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string[]>(Ct)).ShouldBe(["google"]);
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=300");
    }

    [Fact]
    public void The_Development_composition_registers_google_from_its_client_id()
    {
        // The host's scripted adapter reaches the handlers through RegisteredProviders alone, so this is the
        // composition's own registration, made by the gate from the client id in this host's configuration.
        factory.Services.GetServices<IExternalIdentityProvider>().ShouldHaveSingleItem()
            .ShouldBeOfType<GoogleIdentityProvider>();
    }

    [Fact]
    public async Task A_start_hands_its_flow_to_the_state_store()
    {
        // The counter the refusal rows read, observed non-zero on this host, so their zero is a count and not a
        // counter that never moves.
        var store = (FaultableOAuthStateStore)factory.Services.GetRequiredService<IOAuthStateStore>();
        var before = store.Writes;

        await StartAsync();

        store.Writes.ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_start_answers_the_authorize_url_and_the_state_and_nothing_else()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next = "/oversikt" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateObject().Select(p => p.Name)
            .ShouldBe(["authorizeUrl", "state"]);
    }

    [Fact]
    public async Task A_start_past_the_global_budget_is_a_503_that_writes_nothing_and_leaves_a_code_login_open()
    {
        var store = (FaultableOAuthStateStore)factory.Services.GetRequiredService<IOAuthStateStore>();
        for (var i = 0; i < ExternalLoginPolicy.StartBudget.Limit; i++)
            await StartAsync();
        var before = store.Writes;

        var refused = await _client.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next = "/oversikt" }, Ct);

        // The title, not only the status: a 429 from this host's AuthWrite limiter would be a different refusal.
        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()
            .ShouldBe(AuthErrorCodes.ExternalLoginStartsExhausted);
        store.Writes.ShouldBe(before);
        (await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email = NewAddress("after-flood") }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData("github")]
    [InlineData("myspace")]
    public async Task Starting_a_provider_this_host_did_not_register_is_not_found(string provider)
    {
        (await _client.PostAsJsonAsync($"/api/v1/auth/oauth/{provider}/start", new { next = "/oversikt" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("oversikt")]
    public async Task A_path_that_is_not_same_site_is_refused_before_a_flow_is_minted(string next)
    {
        var store = (FaultableOAuthStateStore)factory.Services.GetRequiredService<IOAuthStateStore>();
        var before = store.Writes;

        (await _client.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        store.Writes.ShouldBe(before);
    }

    // ── the state (acceptance: mismatch, replay, expiry refused with no session) ──

    [Fact]
    public async Task An_unknown_a_replayed_and_an_expired_state_are_one_answer_and_open_no_session()
    {
        var unknown = await CallbackAsync("4/0AVGzR1unknown", OAuthState.Generate().Reveal());

        var used = await StartAsync();
        (await CallbackAsync(GoogleAuthorises(used, Workspace(NewSubject(), NewAddress("replay"))), used.State))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = await CallbackAsync(GoogleAuthorises(used, Workspace(NewSubject(), NewAddress("replay2"))), used.State);

        // The clock that ends a flow is Redis's TTL; it is run out here by hand.
        var expiring = await StartAsync();
        await using (var admin = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString))
        {
            await admin.GetDatabase().KeyExpireAsync(
                RedisOAuthStateStore.Key(OAuthState.FromRaw(expiring.State)), TimeSpan.FromMilliseconds(1));
        }

        await Task.Delay(50, Ct);
        var expired = await CallbackAsync(
            GoogleAuthorises(expiring, Workspace(NewSubject(), NewAddress("expired"))), expiring.State);

        var answer = await ComparableAsync(unknown);
        answer.ShouldContain(AuthErrorCodes.ExternalLoginUnusable);
        answer.ShouldStartWith("410|");
        (await ComparableAsync(replayed)).ShouldBe(answer);
        (await ComparableAsync(expired)).ShouldBe(answer);
    }

    [Fact]
    public async Task A_code_google_refuses_is_the_same_answer_and_spends_the_flow()
    {
        var flow = await StartAsync();

        var refused = await CallbackAsync("4/0AVGzR1never-issued", flow.State);
        var unknown = await CallbackAsync("4/0AVGzR1unknown", OAuthState.Generate().Reveal());

        (await ComparableAsync(refused)).ShouldBe(await ComparableAsync(unknown));
        (await CallbackAsync(GoogleAuthorises(flow, Workspace(NewSubject(), NewAddress("spent"))), flow.State))
            .StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    // ── the address (acceptance: unverified refused with the actionable code, no link) ──

    [Fact]
    public async Task An_address_google_is_not_authoritative_for_is_refused_and_links_nothing()
    {
        var address = $"anna-{Guid.NewGuid():N}@outlook.example";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, address, ct: Ct);
        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        var flow = await StartAsync();

        var response = await CallbackAsync(
            GoogleAuthorises(flow, GoogleUserInfoShapes.ThirdPartyVerified(NewSubject(), address)), flow.State);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ComparableAsync(response)).ShouldContain(AuthErrorCodes.ExternalEmailUnverified);
        (await LoginRowsAsync(userId)).ShouldBe(0);
    }

    // ── an existing account (acceptance: linked via AspNetUserLogins, session; the second sign-in finds the login) ──

    [Fact]
    public async Task A_verified_address_of_an_existing_account_links_it_and_signs_in()
    {
        var address = NewAddress("befintlig");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, address, ct: Ct);
        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        var sub = NewSubject();

        var first = await SignInByGoogleAsync(sub, address);
        var second = await SignInByGoogleAsync(sub, address);

        first.GetProperty("outcome").GetString().ShouldBe("signedIn");
        second.GetProperty("outcome").GetString().ShouldBe("signedIn");
        (await MeAsync(second.GetProperty("sessionId").GetString()!)).ShouldBe(HttpStatusCode.OK);
        (await LoginRowsAsync(userId)).ShouldBe(1);
        // The column is jsonb, which re-serialises; the row names the provider and nothing else, never the subject.
        var payload = JsonDocument.Parse((await LinkedAuditPayloadsAsync(userId)).ShouldHaveSingleItem()!).RootElement;
        payload.EnumerateObject().Select(p => p.Name).ShouldBe(["provider"]);
        payload.GetProperty("provider").GetString().ShouldBe("google");
        payload.GetRawText().ShouldNotContain(sub);
    }

    [Fact]
    public async Task A_linked_login_whose_address_changed_at_google_is_refused()
    {
        // test-writer Major 8, the expectation senior-cto-advisor F2 reversed: a Workspace admin renamed the user.
        var address = NewAddress("fore");
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, address, ct: Ct);
        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        var sub = NewSubject();
        (await SignInByGoogleAsync(sub, address)).GetProperty("outcome").GetString().ShouldBe("signedIn");
        var newAddress = NewAddress("efter");

        var renamed = await SignInByGoogleAsync(sub, newAddress);

        renamed.GetProperty("outcome").GetString().ShouldBe("accountUnavailable");
        renamed.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await LoginRowsAsync(userId)).ShouldBe(1);
        (await UserIdOfAsync(newAddress)).ShouldBeNull();
    }

    // ── a new address (acceptance: consent step, then account + link + session; closed registration) ──

    [Fact]
    public async Task A_new_verified_address_waits_for_the_terms_and_then_gets_an_account_a_link_and_a_session()
    {
        var address = NewAddress("ny");
        var sub = NewSubject();
        var flow = await StartAsync(next: "/cv");

        var response = await CallbackAsync(GoogleAuthorises(flow, Workspace(sub, address)), flow.State);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        body.GetProperty("outcome").GetString().ShouldBe("consentRequired");
        body.GetProperty("next").GetString().ShouldBe("/cv");
        (await UserIdOfAsync(address)).ShouldBeNull("no row is written before the terms are accepted (ADR 0142 D3)");

        var grant = body.GetProperty("grantToken").GetString()!;
        (await _client.PostAsJsonAsync("/api/v1/auth/challenge/complete", new { grantToken = grant, acceptTerms = false }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var completed = await _client.PostAsJsonAsync(
            "/api/v1/auth/challenge/complete", new { grantToken = grant, acceptTerms = true }, Ct);
        var completion = await completed.Content.ReadFromJsonAsync<JsonElement>(Ct);

        completion.GetProperty("outcome").GetString().ShouldBe("signedIn");
        var userId = (await UserIdOfAsync(address)).ShouldNotBeNull();
        (await LoginRowsAsync(userId)).ShouldBe(1);
        (await MeAsync(completion.GetProperty("sessionId").GetString()!)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_registration_closed_a_new_verified_address_is_closed_and_gets_no_grant()
    {
        var closed = factory.CreateRegistrationsClosedClient();

        var body = await SignInByGoogleAsync(NewSubject(), NewAddress("stangd"), closed);

        body.GetProperty("outcome").GetString().ShouldBe("registrationClosed");
        body.TryGetProperty("grantToken", out _).ShouldBeFalse();
    }

    // ── availability and policies ──

    [Fact]
    public async Task An_unreachable_redis_answers_503_on_start_and_on_the_callback()
    {
        var flow = await StartAsync();
        using var _ = factory.LoginChallengeFaults.Unavailable();

        (await _client.PostAsJsonAsync("/api/v1/auth/oauth/google/start", new { next = "/oversikt" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await CallbackAsync("4/0AVGzR1code", flow.State)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/api/v1/auth/oauth/{provider}/start", RateLimitingExtensions.AuthWritePolicy)]
    [InlineData("/api/v1/auth/oauth/{provider}/callback", RateLimitingExtensions.AuthWritePolicy)]
    [InlineData("/api/v1/auth/oauth/providers", RateLimitingExtensions.LandingPublicReadPolicy)]
    public void Each_external_login_route_carries_its_rate_limit(string route, string policy)
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == route)
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>().ShouldNotBeNull().PolicyName.ShouldBe(policy);
    }
}
