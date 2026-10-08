using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class AccountAccessLoginFencesTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _public = factory.CreateClient();

    public ValueTask InitializeAsync() => ResetOAuthStartBudgetAsync();
    public ValueTask DisposeAsync() => ResetOAuthStartBudgetAsync();

    private async ValueTask ResetOAuthStartBudgetAsync()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        await redis.GetDatabase().KeyDeleteAsync(
            RedisRateBudget.Key(ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject));
    }

    private sealed record Owner(Guid Id, string Email, string Session);
    private async Task<Owner> OwnerAsync()
    {
        var email = $"fenced-login-{Guid.NewGuid():N}@firma.example";
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return new Owner(await AdminAccountsKit.UserIdAsync(factory, email, Ct), email, session);
    }

    private async Task TransitionAsync(AccountEmailChangeKit.Admin admin, Guid target, bool suspended, bool repeat = false)
    {
        if (repeat)
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        var verb = suspended ? "suspend" : "reinstate";
        var response = await admin.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{target}/{verb}",
            new { reauthGrant = grant }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private sealed record MailedChallenge(string Id, LoginChallengeEmail.CodeAndLink Mail);

    private async Task<MailedChallenge> ChallengeAsync(string email)
    {
        var previous = ReauthTestHelpers.MailsTo(factory, email).Count;
        var response = await _public.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var elapsed = Stopwatch.StartNew();
        while (ReauthTestHelpers.MailsTo(factory, email).Count == previous)
        {
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
            await Task.Delay(25, Ct);
        }
        var mail = ReauthTestHelpers.MailsTo(factory, email)[^1].Content.ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        return new MailedChallenge(accepted.GetProperty("challengeId").GetString()!, mail);
    }

    private Task<HttpResponseMessage> ProveAsync(MailedChallenge challenge, bool link) => link
        ? _public.PostAsJsonAsync("/api/v1/auth/link", new { token = challenge.Mail.Link.Reveal() }, Ct)
        : _public.PostAsJsonAsync("/api/v1/auth/challenge/verify",
            new { challengeId = challenge.Id, code = challenge.Mail.Code.Reveal() }, Ct);

    private static async Task<JsonElement> LoginOutcomeAsync(HttpResponseMessage response, string outcome)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe(outcome);
        return body;
    }

    private async Task ShouldHaveUsableSessionAsync(JsonElement signedIn)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("sessionId").GetString());
        (await _public.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordlessProof_ShouldRemainDeadAfterReinstate_WhenIssuedBeforeSuspension(bool link)
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var old = await ChallengeAsync(owner.Email);
        await TransitionAsync(admin, owner.Id, true);
        await TransitionAsync(admin, owner.Id, false, repeat: true);

        var rejected = await LoginOutcomeAsync(await ProveAsync(old, link), "accountUnavailable");
        rejected.TryGetProperty("sessionId", out _).ShouldBeFalse();

        // The clock expires the address cooldown; this starts a new proof after reinstatement.
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString))
            await redis.GetDatabase().KeyDeleteAsync(RedisRateBudget.Key(
                Jobbliggaren.Application.Auth.LoginChallenges.LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)), owner.Email));
        var fresh = await ChallengeAsync(owner.Email);
        await ShouldHaveUsableSessionAsync(await LoginOutcomeAsync(await ProveAsync(fresh, link), "signedIn"));
    }

    private sealed record OAuthFlow(string Provider, string State, string? Challenge, string RedirectUri,
        string Subject, long NumericSubject);

    private async Task<OAuthFlow> StartAsync(string provider, string? subject = null, long? numericSubject = null)
    {
        var response = await _public.PostAsJsonAsync($"/api/v1/auth/oauth/{provider}/start", new { next = "/ansokningar" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var query = HttpUtility.ParseQueryString(new Uri(body.GetProperty("authorizeUrl").GetString()!).Query);
        return new OAuthFlow(provider, body.GetProperty("state").GetString()!, query["code_challenge"],
            query["redirect_uri"]!, subject ?? LinkedInUserInfoShapes.NewSub(),
            numericSubject ?? Random.Shared.NextInt64(1_000_000, 1L << 53));
    }

    private Task<HttpResponseMessage> CallbackAsync(OAuthFlow flow, string email)
    {
        var code = $"scripted-suspension-{Guid.NewGuid():N}";
        switch (flow.Provider)
        {
            case "google":
                factory.Google.Expect(code, GoogleUserInfoShapes.Workspace(flow.Subject, email, "firma.example"),
                    flow.Challenge, flow.RedirectUri);
                break;
            case "github":
                factory.GitHub.Expect(code, GitHubApiShapes.User(flow.NumericSubject, "suspension-flow"),
                    GitHubApiShapes.Emails.PrimaryVerified(email), flow.Challenge!, flow.RedirectUri);
                break;
            case "linkedin":
                factory.LinkedIn.Expect(code, LinkedInUserInfoShapes.Member(flow.Subject, email),
                    redirectUri: new Uri(flow.RedirectUri));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(flow));
        }
        return _public.PostAsJsonAsync($"/api/v1/auth/oauth/{flow.Provider}/callback", new { code, state = flow.State }, Ct);
    }

    private async Task<int> ProviderRowsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .CountAsync(login => login.UserId == userId, Ct);
    }

    [Theory]
    [InlineData("google", false)]
    [InlineData("github", false)]
    [InlineData("linkedin", false)]
    [InlineData("google", true)]
    [InlineData("github", true)]
    [InlineData("linkedin", true)]
    public async Task OAuthProof_ShouldRemainDeadWithoutProviderLink_WhenStartedBeforeOrDuringSuspension(
        string provider, bool duringSuspension)
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        OAuthFlow old;
        if (duringSuspension)
        {
            await TransitionAsync(admin, owner.Id, true);
            old = await StartAsync(provider);
        }
        else
        {
            old = await StartAsync(provider);
            await TransitionAsync(admin, owner.Id, true);
        }
        await TransitionAsync(admin, owner.Id, false, repeat: true);

        var rejected = await LoginOutcomeAsync(await CallbackAsync(old, owner.Email), "accountUnavailable");
        rejected.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await ProviderRowsAsync(owner.Id)).ShouldBe(0);

        var fresh = await StartAsync(provider, old.Subject, old.NumericSubject);
        await ShouldHaveUsableSessionAsync(await LoginOutcomeAsync(await CallbackAsync(fresh, owner.Email), "signedIn"));
        (await ProviderRowsAsync(owner.Id)).ShouldBe(1);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    [InlineData("linkedin")]
    public async Task OAuthProof_ShouldDenyAnAlreadySuspendedTarget_WhenCallbackResolvesIt(string provider)
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await TransitionAsync(admin, owner.Id, true);
        var started = await StartAsync(provider);

        var body = await LoginOutcomeAsync(await CallbackAsync(started, owner.Email), "accountUnavailable");

        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await ProviderRowsAsync(owner.Id)).ShouldBe(0);
    }

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    [InlineData("linkedin")]
    public async Task OAuthProof_ShouldRemainUsable_WhenOnlyAnotherAccountsAccessChanges(string provider)
    {
        var owner = await OwnerAsync();
        var other = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var original = await StartAsync(provider);

        await TransitionAsync(admin, other.Id, true);

        await ShouldHaveUsableSessionAsync(await LoginOutcomeAsync(await CallbackAsync(original, owner.Email), "signedIn"));
        (await ProviderRowsAsync(owner.Id)).ShouldBe(1);
        (await ProviderRowsAsync(other.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task UnboundConsentGrant_ShouldKeepOriginalEpoch_WhenAnotherFlowRegistersThenTransitionsItsAccount()
    {
        var email = $"unbound-consent-{Guid.NewGuid():N}@firma.example";
        // A fresh unknown-mail window; all actual minting and grant consumption use the production routes.
        await using (var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString))
            await redis.GetDatabase().KeyDeleteAsync(
                RedisRateBudget.Key(LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject));
        var previous = ReauthTestHelpers.MailsTo(factory, email).Count;
        var request = await _public.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("challengeId").GetString();
        var elapsed = Stopwatch.StartNew();
        while (ReauthTestHelpers.MailsTo(factory, email).Count == previous)
        {
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
            await Task.Delay(25, Ct);
        }
        var code = ReauthTestHelpers.MailsTo(factory, email)[^1].Content
            .ShouldBeOfType<LoginChallengeEmail.NewAccountCode>().Code.Reveal();
        var consent = await LoginOutcomeAsync(await _public.PostAsJsonAsync("/api/v1/auth/challenge/verify",
            new { challengeId, code }, Ct), "consentRequired");
        var grantToken = consent.GetProperty("grantToken").GetString();

        // A competing registration, as another proven flow accepting its terms does.
        var target = await AdminAccountsKit.OpenActiveAsync(factory, email, Ct);
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await TransitionAsync(admin, target, true);
        await TransitionAsync(admin, target, false, repeat: true);

        var answer = await LoginOutcomeAsync(await _public.PostAsJsonAsync("/api/v1/auth/challenge/complete",
            new { grantToken, acceptTerms = true }, Ct), "accountUnavailable");
        answer.TryGetProperty("sessionId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task BoundAddressProof_ShouldBeRefusedWithoutAddressMutation_WhenUsedFromANewSessionAfterReinstatement()
    {
        var owner = await OwnerAsync();
        var next = $"new-address-{Guid.NewGuid():N}@firma.example";
        var oldGrant = await ReauthTestHelpers.MintChangeEmailGrantAsync(factory, _public, owner.Session, owner.Email, next, Ct);
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await TransitionAsync(admin, owner.Id, true);
        await TransitionAsync(admin, owner.Id, false, repeat: true);
        var signedIn = await LoginOutcomeAsync(await ProveAsync(await ChallengeAsync(owner.Email), false), "signedIn");
        var currentSession = signedIn.GetProperty("sessionId").GetString()!;

        var refused = await ReauthTestHelpers.ConfirmAddressChangeAsync(_public, currentSession, oldGrant, next, Ct);

        refused.IsSuccessStatusCode.ShouldBeFalse();
        await using var scope = factory.Services.CreateAsyncScope();
        var state = await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == owner.Id).Select(user => new { user.Email, user.UserName }).SingleAsync(Ct);
        state.Email.ShouldBe(owner.Email);
        state.UserName.ShouldBe(owner.Email);
        (await scope.ServiceProvider.GetRequiredService<Jobbliggaren.Infrastructure.Persistence.AppDbContext>().AuditLogEntries
            .AnyAsync(row => row.AggregateId == owner.Id && row.EventType == "User.EmailChanged", Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("code", false)]
    [InlineData("link", false)]
    [InlineData("google", false)]
    [InlineData("github", false)]
    [InlineData("linkedin", false)]
    [InlineData("code", true)]
    [InlineData("link", true)]
    [InlineData("google", true)]
    [InlineData("github", true)]
    [InlineData("linkedin", true)]
    public async Task ProvenLogin_ShouldRefuseTheFormerInbox_WhenAddressChangesBeforeAdmissionOrIssuance(
        string method, bool afterProofCommit)
    {
        var owner = await OwnerAsync();
        var next = $"moved-during-login-{Guid.NewGuid():N}@firma.example";
        var addressGrant = await ReauthTestHelpers.MintChangeEmailGrantAsync(
            factory, _public, owner.Session, owner.Email, next, Ct);
        OAuthFlow? oauth = null;
        MailedChallenge? challenge = null;
        if (method is "code" or "link")
            challenge = await ChallengeAsync(owner.Email);
        else
            oauth = await StartAsync(method);
        using var gate = afterProofCommit
            ? factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id)
            : factory.AccountAccessFlowGates.PauseBeforeAdmission(owner.Id);
        var login = challenge is not null
            ? ProveAsync(challenge, method == "link")
            : CallbackAsync(oauth.ShouldNotBeNull(), owner.Email);
        var heldProof = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        if (afterProofCommit)
        {
            heldProof.ShouldNotBeNull().ExpectedEmail.ShouldBe(owner.Email);
            heldProof.UserId.ShouldBe(owner.Id);
            heldProof.AccessRevision.ShouldBe(0);
        }

        var moved = await ReauthTestHelpers.ConfirmAddressChangeAsync(_public, owner.Session, addressGrant, next, Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct));
        gate.Release();
        var refused = await LoginOutcomeAsync(await login.WaitAsync(TimeSpan.FromSeconds(30), Ct), "accountUnavailable");
        refused.TryGetProperty("sessionId", out _).ShouldBeFalse();
        await using (var read = factory.Services.CreateAsyncScope())
        {
            var state = (await read.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(owner.Id, Ct))
                .ShouldNotBeNull();
            state.Email.ShouldBe(next);
            state.AccessRevision.ShouldBe(1);
        }
        (await ProviderRowsAsync(owner.Id)).ShouldBe(oauth is not null && afterProofCommit ? 1 : 0);

        if (oauth is not null)
        {
            var fresh = await StartAsync(method, oauth.Subject, oauth.NumericSubject);
            await ShouldHaveUsableSessionAsync(await LoginOutcomeAsync(await CallbackAsync(fresh, next), "signedIn"));
        }
        else
        {
            var fresh = await ChallengeAsync(next);
            await ShouldHaveUsableSessionAsync(await LoginOutcomeAsync(await ProveAsync(fresh, method == "link"), "signedIn"));
        }
    }

    [Theory]
    [InlineData("code", false)]
    [InlineData("link", false)]
    [InlineData("google", false)]
    [InlineData("github", false)]
    [InlineData("linkedin", false)]
    [InlineData("code", true)]
    [InlineData("link", true)]
    [InlineData("google", true)]
    [InlineData("github", true)]
    [InlineData("linkedin", true)]
    public async Task ProvenLogin_ShouldIssueNoSessionOrProviderLink_WhenAdminDeletionWinsBeforeAdmissionOrIssuance(
        string method, bool afterProofCommit)
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        OAuthFlow? oauth = null;
        MailedChallenge? challenge = null;
        if (method is "code" or "link") challenge = await ChallengeAsync(owner.Email);
        else oauth = await StartAsync(method);
        using var gate = afterProofCommit
            ? factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id)
            : factory.AccountAccessFlowGates.PauseBeforeAdmission(owner.Id);
        var login = challenge is not null
            ? ProveAsync(challenge, method == "link")
            : CallbackAsync(oauth.ShouldNotBeNull(), owner.Email);
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        (await admin.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{owner.Id}/deletion", new { reauthGrant = grant }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        gate.Release();
        var refused = await LoginOutcomeAsync(await login.WaitAsync(TimeSpan.FromSeconds(30), Ct), "accountUnavailable");
        refused.TryGetProperty("sessionId", out _).ShouldBeFalse();
        (await ProviderRowsAsync(owner.Id)).ShouldBe(0);
        using var oldSessionClient = factory.CreateClient();
        oldSessionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Session);
        (await oldSessionClient.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
