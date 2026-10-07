using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class CredentialTransitionSessionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();
    private sealed record Owner(Guid Id, string Email, string Session);
    private static string Address() => $"credential-transition-{Guid.NewGuid():N}@example.se";

    private async Task<Owner> OwnerAsync()
    {
        var email = Address();
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return new Owner(await AdminAccountsKit.UserIdAsync(factory, email, Ct), email, session);
    }

    private async Task<AccountAccessSnapshot> StateAsync(Guid userId)
    {
        await using var read = factory.Services.CreateAsyncScope();
        return (await read.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(userId, Ct)).ShouldNotBeNull();
    }

    private async Task<int> AuditsAsync(Guid userId, string eventType)
    {
        await using var read = factory.Services.CreateAsyncScope();
        return await read.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .CountAsync(row => row.AggregateId == userId && row.EventType == eventType, Ct);
    }

    private async Task<HttpStatusCode> MeAsync(string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return (await _client.SendAsync(request, Ct)).StatusCode;
    }

    private sealed record Challenge(string Id, LoginChallengeEmail.CodeAndLink Mail);
    private async Task<Challenge> ChallengeAsync(string email)
    {
        var count = ReauthTestHelpers.MailsTo(factory, email).Count;
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var wait = Stopwatch.StartNew();
        while (ReauthTestHelpers.MailsTo(factory, email).Count == count)
        {
            wait.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
            await Task.Delay(25, Ct);
        }
        return new Challenge(body.GetProperty("challengeId").GetString()!,
            ReauthTestHelpers.MailsTo(factory, email)[^1].Content.ShouldBeOfType<LoginChallengeEmail.CodeAndLink>());
    }

    private Task<HttpResponseMessage> ProveAsync(Challenge challenge, bool link) => link
        ? _client.PostAsJsonAsync("/api/v1/auth/link", new { token = challenge.Mail.Link.Reveal() }, Ct)
        : _client.PostAsJsonAsync("/api/v1/auth/challenge/verify",
            new { challengeId = challenge.Id, code = challenge.Mail.Code.Reveal() }, Ct);

    private static async Task<string> SignedInAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe("signedIn");
        return body.GetProperty("sessionId").GetString().ShouldNotBeNull();
    }

    private async Task<string> FreshLoginAsync(string email) =>
        await SignedInAsync(await ProveAsync(await ChallengeAsync(email), link: false));

    private static async Task NoIssuanceAsync(HttpResponseMessage response)
    {
        (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Gone)
            .ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        body.TryGetProperty("grantToken", out _).ShouldBeFalse();
    }

    private async Task LetLoginCooldownExpireAsync(string email)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        // The clock expires the actual request's cooldown key; the proof itself is never rewritten.
        (await redis.GetDatabase().KeyDeleteAsync(RedisRateBudget.Key(
            LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)), email))).ShouldBeTrue();
    }

    private static string SessionKey(string raw) => "jobbliggaren:session:v2:" +
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task ShouldRetainRedisButDenyOldSessionAsync(string oldSession)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.DurableRedisConnectionString);
        (await redis.GetDatabase().KeyExistsAsync(SessionKey(oldSession))).ShouldBeTrue();
        await using var read = factory.Services.CreateAsyncScope();
        var sessions = read.ServiceProvider.GetRequiredService<ISessionStore>();
        (await sessions.GetAsync(SessionId.FromRaw(oldSession), Ct)).ShouldBeNull();
        (await sessions.RotateAsync(SessionId.FromRaw(oldSession), Ct)).ShouldBeNull();
        (await MeAsync(oldSession)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<string> AddressGrantAsync(Owner owner, string next) =>
        await ReauthTestHelpers.MintChangeEmailGrantAsync(factory, _client, owner.Session, owner.Email, next, Ct);

    private async Task<Owner> WithSessionLifetimeAsync(Owner owner, SessionLifetime lifetime)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountAccessReader>();
        var account = (await reader.ReadAsync(owner.Id, Ct)).ShouldNotBeNull();
        account.CanAuthenticate.ShouldBeTrue();
        var original = new AccountAccessProof(await reader.ReadEpochAsync(Ct), owner.Id, account.AccessRevision);
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var created = (await sessions.CreateAsync(owner.Id, original, lifetime, Ct)).ShouldNotBeNull();
        created.Lifetime.ShouldBe(lifetime);
        var stored = (await sessions.GetAsync(created.Id, Ct)).ShouldNotBeNull();
        stored.Lifetime.ShouldBe(lifetime);
        stored.AccessRevision.ShouldBe(account.AccessRevision);
        return owner with { Session = created.Id.Reveal() };
    }

    [Theory]
    [InlineData(false, SessionLifetime.Session)]
    [InlineData(true, SessionLifetime.Session)]
    [InlineData(false, SessionLifetime.Persistent)]
    [InlineData(true, SessionLifetime.Persistent)]
    public async Task AddressChange_ShouldFenceOldProofsAndPreserveFreshSessions_WhenRedisCleanupFailsThenArrivesLate(
        bool link, SessionLifetime lifetime)
    {
        var owner = await WithSessionLifetimeAsync(await OwnerAsync(), lifetime);
        var before = await StateAsync(owner.Id);
        var next = Address();
        var grant = await AddressGrantAsync(owner, next);
        var oldProof = await ChallengeAsync(owner.Email);
        HttpResponseMessage response;
        using (factory.SessionTeardownFaults.FailingFor(owner.Id))
            response = await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session, grant, next, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("persistent").GetBoolean().ShouldBe(lifetime == SessionLifetime.Persistent);
        var committedSession = body.GetProperty("sessionId").GetString().ShouldNotBeNull();
        var after = await StateAsync(owner.Id);
        after.AccessRevision.ShouldBe(before.AccessRevision + 1);
        after.CredentialCutoff.ShouldBeGreaterThan(before.CredentialCutoff);
        after.Email.ShouldBe(next);
        await ShouldRetainRedisButDenyOldSessionAsync(owner.Session);
        await NoIssuanceAsync(await ProveAsync(oldProof, link));
        var fresh = await FreshLoginAsync(next);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            // The delayed endpoint cleanup carries the revision of this actual committed transition.
            await sessions.InvalidateBeforeRevisionAsync(owner.Id, after.AccessRevision, Ct);
            var replacement = (await sessions.GetAsync(SessionId.FromRaw(committedSession), Ct)).ShouldNotBeNull();
            replacement.AccessRevision.ShouldBe(after.AccessRevision);
            replacement.Lifetime.ShouldBe(lifetime);
            (await sessions.GetAsync(SessionId.FromRaw(fresh), Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(after.AccessRevision);
        }
        (await MeAsync(committedSession)).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(fresh)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CommittedSessionAuthorization_ShouldRefuseLaterGeneration_WhenSuspensionRunsBeforeSpecialIssuance()
    {
        var owner = await OwnerAsync();
        var next = Address();
        var grant = await AddressGrantAsync(owner, next);
        var administrator = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id);
        var confirmation = ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session, grant, next, Ct);
        // ISessionStore's default special dispatch reaches the ordinary proof gate and the real primary guard.
        var committed = (await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldNotBeNull();
        var changed = await StateAsync(owner.Id);
        committed.UserId.ShouldBe(owner.Id);
        committed.AccessRevision.ShouldBe(changed.AccessRevision);
        committed.ExpectedEmail.ShouldBe(next);
        committed.ExpectedCutoff.ShouldBe(changed.CredentialCutoff);
        committed.FlowEpoch.ShouldBe(changed.CredentialCutoff);
        foreach (var verb in new[] { "suspend", "reinstate" })
        {
            if (verb == "reinstate")
                await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, administrator.Email, Ct);
            var stepup = await ReauthTestHelpers.MintGrantAsync(factory, administrator.Client,
                administrator.SessionId, administrator.Email, Ct);
            (await administrator.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{owner.Id}/{verb}", new { reauthGrant = stepup }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        gate.Release();

        var refused = await confirmation.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("title").GetString().ShouldBe("Auth.SessionUnavailable");
        problem.TryGetProperty("sessionId", out _).ShouldBeFalse();
        var current = await StateAsync(owner.Id);
        current.Email.ShouldBe(next);
        current.AccessRevision.ShouldBe(changed.AccessRevision + 2);
        committed.Admits(current).ShouldBeFalse();
        (await AuditsAsync(owner.Id, "User.EmailChanged")).ShouldBe(1);
        (await MeAsync(await FreshLoginAsync(next))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddressChange_ShouldIssueNothingAndRollBackEpoch_WhenAuditSaveFails()
    {
        var owner = await OwnerAsync();
        var next = Address();
        var grant = await AddressGrantAsync(owner, next);
        var before = await StateAsync(owner.Id);
        await using var read = factory.Services.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<IAccountAccessReader>();
        var epoch = await reader.ReadEpochAsync(Ct);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id);
        HttpResponseMessage response;
        using (factory.AuditRowSaveFailure.FailingFor("User.EmailChanged", owner.Id))
            response = await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session, grant, next, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        gate.Reached.Task.IsCompleted.ShouldBeFalse();
        (await StateAsync(owner.Id)).ShouldBe(before);
        (await reader.ReadEpochAsync(Ct)).ShouldBe(epoch);
        (await AuditsAsync(owner.Id, "User.EmailChanged")).ShouldBe(0);
        (await MeAsync(owner.Session)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddressChange_ShouldReturnUnknownWithoutSession_WhenActualCommitAcknowledgementIsLost()
    {
        var owner = await OwnerAsync();
        var next = Address();
        var grant = await AddressGrantAsync(owner, next);
        var before = await StateAsync(owner.Id);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id);
        HttpResponseMessage response;
        using (factory.CommitAcknowledgementLoss.AfterAddressChangeCommit(owner.Id))
            response = await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session, grant, next, Ct);

        factory.CommitAcknowledgementLoss.Fired.ShouldBe(1);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("title").GetString().ShouldBe("Admin.AccountAccessOutcomeUnknown");
        problem.TryGetProperty("sessionId", out _).ShouldBeFalse();
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        gate.Reached.Task.IsCompleted.ShouldBeFalse();
        var observed = await StateAsync(owner.Id);
        observed.Email.ShouldBe(next);
        observed.AccessRevision.ShouldBe(before.AccessRevision + 1);
        (await AuditsAsync(owner.Id, "User.EmailChanged")).ShouldBe(1);
        (await MeAsync(owner.Session)).ShouldBe(HttpStatusCode.Unauthorized);
        // A separate fresh proof can log in; the uncertain command never receives authority retroactively.
        gate.Dispose();
        (await MeAsync(await FreshLoginAsync(next))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReauthenticationRefusal_ShouldPreserveNewSessions_WhenAnOlderInFlightRequestResumesAfterAddressChange()
    {
        var owner = await OwnerAsync();
        var staleGrant = await ReauthTestHelpers.MintGrantAsync(factory, _client, owner.Session, owner.Email, Ct);
        await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, owner.Email, Ct);
        var next = Address();
        var addressGrant = await AddressGrantAsync(owner, next);
        using var gate = factory.AccountAccessFlowGates.PauseAfterReauthenticationProof(owner.Id);
        var oldRequest = ReauthTestHelpers.PostAsSessionAsync(_client, owner.Session, "/api/v1/auth/change-email",
            new { reauthGrant = staleGrant, newEmail = Address() }, Ct);
        var stale = (await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldNotBeNull();
        var moved = await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session, addressGrant, next, Ct);
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var committedSession = (await moved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("sessionId").GetString()!;
        var fresh = await FreshLoginAsync(next);
        stale.Admits(await StateAsync(owner.Id)).ShouldBeFalse();
        gate.Release();

        (await oldRequest.WaitAsync(TimeSpan.FromSeconds(30), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(committedSession)).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(fresh)).ShouldBe(HttpStatusCode.OK);
        (await AuditsAsync(owner.Id, "User.EmailChanged")).ShouldBe(1);
    }

    // Retired /auth/register followed by NullPasswordHashes produced this unconfirmed, hash-free account.
    // AccountRegistrationAtomicityTests.OpenAsync_ShouldCommitIdentityProfileAndAudit_WhenRegistrationSucceeds
    // pins that today's registrar always confirms the address.
    private async Task<Owner> RetiredUnconfirmedAccountAsync()
    {
        var email = Address();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = false };
        await using var scope = factory.Services.CreateAsyncScope();
        await using (var held = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>().BeginAsync([user.Id], false, Ct))
        {
            (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded.ShouldBeTrue();
            await held.CommitAsync(Ct);
        }
        var session = await AuthTestHelpers.RegisterJobSeekerAndCreateSessionAsync(scope.ServiceProvider, user.Id, SessionLifetime.Persistent, Ct);
        return new Owner(user.Id, email, session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstInboxProof_ShouldFenceOldSessionsAndKeepNewGeneration_WhenCleanupFailsThenArrivesLate(bool link)
    {
        var owner = await RetiredUnconfirmedAccountAsync();
        var before = await StateAsync(owner.Id);
        var challenge = await ChallengeAsync(owner.Email);
        string provenSession;
        using (factory.SessionTeardownFaults.FailingFor(owner.Id))
            provenSession = await SignedInAsync(await ProveAsync(challenge, link));
        var after = await StateAsync(owner.Id);
        after.AccessRevision.ShouldBe(before.AccessRevision + 1);
        after.CredentialCutoff.ShouldBeGreaterThan(before.CredentialCutoff);
        await ShouldRetainRedisButDenyOldSessionAsync(owner.Session);
        await NoIssuanceAsync(await ProveAsync(challenge, !link));
        await LetLoginCooldownExpireAsync(owner.Email);
        var fresh = await FreshLoginAsync(owner.Email);
        await using var read = factory.Services.CreateAsyncScope();
        var sessions = read.ServiceProvider.GetRequiredService<ISessionStore>();
        await sessions.InvalidateBeforeRevisionAsync(owner.Id, after.AccessRevision, Ct);
        (await sessions.GetAsync(SessionId.FromRaw(provenSession), Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(after.AccessRevision);
        (await sessions.GetAsync(SessionId.FromRaw(fresh), Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(after.AccessRevision);
        (await AuditsAsync(owner.Id, PasswordlessSessionGrant.InboxProvenAuditEventType)).ShouldBe(1);
        (await read.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == owner.Id).Select(user => user.EmailConfirmed).SingleAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task FirstInboxProof_ShouldRollBackConfirmationAndGeneration_WhenItsAuditSaveFails()
    {
        var owner = await RetiredUnconfirmedAccountAsync();
        var before = await StateAsync(owner.Id);
        var challenge = await ChallengeAsync(owner.Email);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id);
        HttpResponseMessage response;
        using (factory.AuditRowSaveFailure.FailingFor(PasswordlessSessionGrant.InboxProvenAuditEventType, owner.Id))
            response = await ProveAsync(challenge, link: false);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        gate.Reached.Task.IsCompleted.ShouldBeFalse();
        (await StateAsync(owner.Id)).ShouldBe(before);
        (await AuditsAsync(owner.Id, PasswordlessSessionGrant.InboxProvenAuditEventType)).ShouldBe(0);
        await using (var read = factory.Services.CreateAsyncScope())
            (await read.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
                .Where(user => user.Id == owner.Id).Select(user => user.EmailConfirmed).SingleAsync(Ct)).ShouldBeFalse();
        (await MeAsync(owner.Session)).ShouldBe(HttpStatusCode.OK);
        gate.Dispose();
        await LetLoginCooldownExpireAsync(owner.Email);
        (await MeAsync(await FreshLoginAsync(owner.Email))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicAdministratorAddressCompletion_ShouldIssueNoSession_WhenCleanupFailsAfterCommit()
    {
        var owner = await OwnerAsync();
        var next = Address();
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.Id, next, owner.Email, Ct);
        var before = await StateAsync(owner.Id);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeSession(owner.Id);
        HttpResponseMessage response;
        using (factory.SessionTeardownFaults.FailingFor(owner.Id))
            response = await AccountEmailChangeKit.CompleteAsync(_client, owner.Email, next, pending.Code.Reveal(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBeEmpty();
        gate.Reached.Task.IsCompleted.ShouldBeFalse();
        var after = await StateAsync(owner.Id);
        after.AccessRevision.ShouldBe(before.AccessRevision + 1);
        after.Email.ShouldBe(next);
        await ShouldRetainRedisButDenyOldSessionAsync(owner.Session);
        (await AuditsAsync(owner.Id, "User.EmailChangedViaAdministrator")).ShouldBe(1);
    }
}
