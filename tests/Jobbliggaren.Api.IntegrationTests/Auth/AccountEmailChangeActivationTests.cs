using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class AccountEmailChangeActivationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client = factory.CreateClient();
    private EmailChangeActivationFaults Faults => factory.EmailChangeActivationFaults;
    private static string Address() => $"activation-{Guid.NewGuid():N}@example.se";

    private sealed record Owner(Guid Id, string Email, string Session);
    private sealed record RequestJourney(Owner Owner, string NewEmail, string ReauthGrant,
        AccountEmailChangeKit.Admin? Administrator)
    {
        internal bool IsAdmin => Administrator is not null;
        internal string EventType => IsAdmin ? RequestAccountEmailChangeCommand.RequestedEventType
            : ChangeEmailCommand.RequestedEventType;
    }

    private async Task<Owner> OwnerAsync()
    {
        var email = Address();
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        var owner = new Owner(await AdminAccountsKit.UserIdAsync(factory, email, Ct), email, session);
        (await StateAsync(owner.Id)).InboxConfirmed.ShouldBeTrue();
        return owner;
    }

    private async Task<RequestJourney> JourneyAsync(bool admin, Owner? owner = null)
    {
        owner ??= await OwnerAsync();
        var administrator = admin ? await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct) : null;
        var grant = await ReauthTestHelpers.MintGrantAsync(factory,
            administrator?.Client ?? _client, administrator?.SessionId ?? owner.Session,
            administrator?.Email ?? owner.Email, Ct);
        return new RequestJourney(owner, Address(), grant, administrator);
    }

    private Task<HttpResponseMessage> RequestAsync(RequestJourney journey) => journey.Administrator is { } admin
        ? admin.Client.PostAsJsonAsync(AccountEmailChangeKit.Path(journey.Owner.Id),
            new { newEmail = journey.NewEmail, reauthGrant = journey.ReauthGrant }, Ct)
        : ReauthTestHelpers.PostAsSessionAsync(_client, journey.Owner.Session, "/api/v1/auth/change-email",
            new { newEmail = journey.NewEmail, reauthGrant = journey.ReauthGrant }, Ct);

    private async Task<AccountAccessSnapshot> StateAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(id, Ct)).ShouldNotBeNull();
    }

    private async Task<List<AuditLogEntry>> RequestRowsAsync(RequestJourney journey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == journey.Owner.Id && row.EventType == journey.EventType)
            .OrderBy(row => row.OccurredAt).ToListAsync(Ct);
    }

    private async Task AssertExactWitnessAsync(RequestJourney journey)
    {
        var row = (await RequestRowsAsync(journey)).ShouldHaveSingleItem();
        row.AggregateType.ShouldBe("User");
        row.UserId.ShouldBe(journey.Administrator?.UserId ?? journey.Owner.Id);
        var payload = JsonSerializer.Deserialize<JsonElement>(row.Payload.ShouldNotBeNull());
        payload.EnumerateObject().Select(property => property.Name).ShouldBe(["requestId"]);
        if (journey.IsAdmin)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var pending = (await scope.ServiceProvider.GetRequiredService<IAccountEmailChangeStore>()
                .FindPendingAsync(journey.Owner.Id, Ct)).ShouldNotBeNull();
            payload.GetProperty("requestId").GetGuid().ShouldBe(pending.RequestId.ShouldNotBeNull());
            pending.AccessRevision.ShouldBe((await StateAsync(journey.Owner.Id)).AccessRevision);
        }
        else
        {
            var bound = Faults.BoundRequest(journey.NewEmail);
            payload.GetProperty("requestId").GetString().ShouldBe(bound.Id.Reveal());
            bound.EmailChangeRequest.ShouldNotBeNull().RequestId.ShouldBe(bound.Id.Reveal());
            bound.EmailChangeRequest.IsValid.ShouldBeTrue();
            bound.Access.AccessRevision.ShouldBe((await StateAsync(journey.Owner.Id)).AccessRevision);
        }
        row.Payload.ShouldNotContain(journey.Owner.Email);
        row.Payload.ShouldNotContain(journey.NewEmail);
        row.Payload.ShouldNotContain(journey.ReauthGrant);
    }

    private static async Task<HttpResponseMessage> FinishAsync(Task<HttpResponseMessage> request) =>
        await request.WaitAsync(Deadline, Ct);

    private async Task<string> FreshConfirmedLoginAsync(Owner owner)
    {
        var before = ReauthTestHelpers.MailsTo(factory, owner.Email).Count;
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email = owner.Email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var challenge = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("challengeId").GetString()!;
        var elapsed = Stopwatch.StartNew();
        while (ReauthTestHelpers.MailsTo(factory, owner.Email).Count == before)
        {
            elapsed.Elapsed.ShouldBeLessThan(Deadline);
            await Task.Delay(25, Ct);
        }
        var mail = ReauthTestHelpers.MailsTo(factory, owner.Email)[^1].Content
            .ShouldBeOfType<LoginChallengeEmail.CodeAndLink>();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/challenge/verify",
            new { challengeId = challenge, code = mail.Code.Reveal() }, Ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync(Ct));
        var body = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe("signedIn");
        var session = body.GetProperty("sessionId").GetString().ShouldNotBeNull();
        await using var read = factory.Services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<ISessionStore>().GetAsync(SessionId.FromRaw(session), Ct))
            .ShouldNotBeNull().UserId.ShouldBe(owner.Id);
        return session;
    }

    private async Task<HttpResponseMessage> TransitionAsync(AccountEmailChangeKit.Admin administrator, Guid target,
        string verb, string? existingGrant = null)
    {
        var grant = existingGrant ?? await ReauthTestHelpers.MintGrantAsync(factory, administrator.Client,
            administrator.SessionId, administrator.Email, Ct);
        return await administrator.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{target}/{verb}",
            new { reauthGrant = grant }, Ct);
    }

    private async Task ProveUnrelatedWorkCommitsAsync(Owner confirmed, Owner target,
        AccountEmailChangeKit.Admin administrator, string grant)
    {
        var beforeLogin = await StateAsync(confirmed.Id);
        var beforeTarget = await StateAsync(target.Id);
        var login = FreshConfirmedLoginAsync(confirmed);
        var suspended = TransitionAsync(administrator, target.Id, "suspend", grant);
        await Task.WhenAll(login, suspended).WaitAsync(Deadline, Ct);
        (await suspended).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(confirmed.Id)).ShouldBe(beforeLogin);
        var committed = await StateAsync(target.Id);
        committed.IsSuspended.ShouldBeTrue();
        committed.AccessRevision.ShouldBe(beforeTarget.AccessRevision + 1);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .CountAsync(row => row.AggregateId == target.Id && row.EventType == "Admin.AccountSuspended", Ct)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Request_ShouldReleaseEveryDatabaseScope_WhenItsRealMailTransportWaits(bool admin, bool warning)
    {
        var journey = await JourneyAsync(admin);
        var confirmed = await OwnerAsync();
        var target = await OwnerAsync();
        var unrelated = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var unrelatedGrant = await ReauthTestHelpers.MintGrantAsync(factory, unrelated.Client,
            unrelated.SessionId, unrelated.Email, Ct);
        var before = await StateAsync(journey.Owner.Id);
        using var gate = Faults.PauseTransport(warning ? journey.Owner.Email : journey.NewEmail, warning);
        var request = RequestAsync(journey);
        var transport = await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        try
        {
            transport.HasActiveScope.ShouldBeFalse();
            transport.HasLifecycleScope.ShouldBeFalse();
            request.IsCompleted.ShouldBeFalse();
            (await RequestRowsAsync(journey)).ShouldBeEmpty();
            if (warning)
                ReauthTestHelpers.MailsTo(factory, journey.NewEmail).ShouldBeEmpty();
            await ProveUnrelatedWorkCommitsAsync(confirmed, target, unrelated, unrelatedGrant);
            request.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            gate.Release();
        }

        (await FinishAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await StateAsync(journey.Owner.Id)).ShouldBe(before);
        await AssertExactWitnessAsync(journey);
        ReauthTestHelpers.MailsTo(factory, journey.NewEmail).Count.ShouldBe(1);
        if (admin)
            factory.Emails.Sent.Where(mail => mail.ToEmail == journey.Owner.Email || mail.ToEmail == journey.NewEmail)
                .Select(mail => mail.Kind).ShouldBe(
                    [RecordedEmailKind.AccountEmailChangeRequestedNotification, RecordedEmailKind.LoginChallenge]);
    }

    private async Task<RedisValue> BoundAttemptsAsync(ChallengeId id)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        return await redis.GetDatabase().HashGetAsync(
            RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(id)), "a");
    }

    private Task<HttpResponseMessage> VerifyAsync(RequestJourney journey, string code) =>
        ReauthTestHelpers.VerifyAddressChangeAsync(_client, journey.Owner.Session,
            Faults.BoundRequest(journey.NewEmail).Id.Reveal(), code, Ct);

    private string SelfCode(RequestJourney journey) => ReauthTestHelpers.MailsTo(factory, journey.NewEmail)[^1]
        .Content.ShouldBeOfType<LoginChallengeEmail.AddressChangeCode>().Code.Reveal();

    private static async Task RefusesGrantAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeFalse(await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.TryGetProperty("changeEmailGrant", out _).ShouldBeFalse();
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Verify_ShouldPreserveTheRealCodeAndAttemptBudget_WhenMailArrivesBeforeActivationCommits()
    {
        var journey = await JourneyAsync(admin: false);
        using var gate = Faults.PauseTransport(journey.NewEmail);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        var bound = Faults.BoundRequest(journey.NewEmail);
        var code = SelfCode(journey);
        try
        {
            for (var i = 0; i <= LoginChallengePolicy.MaxAttempts; i++)
            {
                var presentation = i % 2 == 0 ? code : code == "000000" ? "111111" : "000000";
                var refused = await VerifyAsync(journey, presentation);
                refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                await RefusesGrantAsync(refused);
            }
            Faults.Consumptions(bound.Id).ShouldBe(0);
            (await BoundAttemptsAsync(bound.Id)).ToString().ShouldBe("0");
            (await RequestRowsAsync(journey)).ShouldBeEmpty();
        }
        finally
        {
            gate.Release();
        }
        (await FinishAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await AssertExactWitnessAsync(journey);
        var verified = await VerifyAsync(journey, code);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var grant = (await verified.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changeEmailGrant")
            .GetString().ShouldNotBeNull();
        Faults.Consumptions(bound.Id).ShouldBe(1);
        (await BoundAttemptsAsync(bound.Id)).IsNull.ShouldBeTrue();
        var changed = await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, journey.Owner.Session, grant,
            journey.NewEmail, Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(journey.Owner.Id)).Email.ShouldBe(journey.NewEmail);
    }

    private async Task<bool> HasPhysicalCredentialAsync(RequestJourney journey)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return journey.IsAdmin
            ? await scope.ServiceProvider.GetRequiredService<IAccountEmailChangeStore>()
                .FindPendingAsync(journey.Owner.Id, Ct) is not null
            : await scope.ServiceProvider.GetRequiredService<ILoginChallengeStore>()
                .ReadEmailChangeRequestAsync(Faults.BoundRequest(journey.NewEmail).Id, journey.Owner.Id, Ct) is not null;
    }

    // The clock ages the exact production-minted record; it preserves its code, nonce and generation.
    private async Task AgeAdminRecordAsync(RequestJourney journey, int hoursAgo = 73)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var key = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(journey.NewEmail));
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisAccountEmailChangeStore.ProtectorPurpose);
        var bytes = (byte[]?)(await db.HashGetAsync(key, "p"));
        bytes.ShouldNotBeNull();
        var original = JsonSerializer.Deserialize<RedisAccountEmailChangeStore.ChangePayload>(protector.Unprotect(bytes))
            .ShouldNotBeNull();
        original.UserId.ShouldBe(journey.Owner.Id);
        var delta = TimeSpan.FromHours(hoursAgo);
        var elapsed = original with
        {
            NotBefore = original.NotBefore - checked((long)delta.TotalMilliseconds),
            ExpiresAt = original.ExpiresAt - checked((long)delta.TotalMilliseconds),
        };
        (elapsed with { NotBefore = original.NotBefore, ExpiresAt = original.ExpiresAt }).ShouldBe(original);
        await db.HashSetAsync(key,
        [
            new HashEntry("p", protector.Protect(JsonSerializer.SerializeToUtf8Bytes(elapsed))),
            new HashEntry("n", elapsed.NotBefore),
            new HashEntry("x", elapsed.ExpiresAt),
        ]);
        var left = DateTimeOffset.FromUnixTimeMilliseconds(elapsed.ExpiresAt)
            - factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow;
        foreach (var record in new[] { key, RedisAccountEmailChangeStore.IndexKey(journey.Owner.Id) })
            (await db.KeyExpireAsync(record, TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds)))).ShouldBeTrue();
        await using var scope = factory.Services.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var witnesses = await app.AuditLogEntries.AsNoTracking().Where(row => row.AggregateId == journey.Owner.Id
            && row.EventType == journey.EventType).ToListAsync(Ct);
        var witness = witnesses.SingleOrDefault(row => JsonSerializer.Deserialize<JsonElement>(row.Payload!)
            .GetProperty("requestId").GetGuid() == original.RequestId);
        if (witness is not null)
            (await app.AuditLogEntries.Where(row => row.Id == witness.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.OccurredAt, witness.OccurredAt - delta), Ct)).ShouldBe(1);
    }

    private async Task AssertCredentialInertAsync(RequestJourney journey)
    {
        (await HasPhysicalCredentialAsync(journey)).ShouldBeTrue();
        if (journey.IsAdmin)
        {
            await AgeAdminRecordAsync(journey);
            var code = ReauthTestHelpers.MailsTo(factory, journey.NewEmail)[^1].Content
                .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>().Code.Reveal();
            var refused = await AccountEmailChangeKit.CompleteAsync(_client, journey.Owner.Email, journey.NewEmail, code, Ct);
            refused.StatusCode.ShouldBe(HttpStatusCode.Gone);
            (await journey.Administrator!.Client.GetAsync(AccountEmailChangeKit.Path(journey.Owner.Id), Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        else
        {
            await RefusesGrantAsync(await VerifyAsync(journey, SelfCode(journey)));
            Faults.Consumptions(Faults.BoundRequest(journey.NewEmail).Id).ShouldBe(0);
        }
        (await StateAsync(journey.Owner.Id)).Email.ShouldBe(journey.Owner.Email);
        (await RequestRowsAsync(journey)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldLeaveAnAcceptedCodeInert_WhenTransportThrowsAndExactCleanupAlsoFails(bool admin)
    {
        var journey = await JourneyAsync(admin);
        var before = await StateAsync(journey.Owner.Id);
        using var gate = Faults.PauseTransport(journey.NewEmail, throwAfterAccepted: true);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        using var cleanupFailure = admin ? factory.AccountEmailChangeStoreFaults.FailingRevocation()
            : Faults.FailingBoundRevocation(Faults.BoundRequest(journey.NewEmail).Id);
        gate.Release();
        (await FinishAsync(request)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        ReauthTestHelpers.MailsTo(factory, journey.NewEmail).Count.ShouldBe(1);
        await AssertCredentialInertAsync(journey);
        (await StateAsync(journey.Owner.Id)).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldCommitNoActivationWitness_WhenItsFinalAuditSaveFails(bool admin)
    {
        var journey = await JourneyAsync(admin);
        var before = await StateAsync(journey.Owner.Id);
        using var gate = Faults.PauseTransport(journey.NewEmail);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        using var saveFailure = Faults.AuditSaveFailure.FailingFor(journey.EventType, journey.Owner.Id);
        using var cleanupFailure = admin ? factory.AccountEmailChangeStoreFaults.FailingRevocation()
            : Faults.FailingBoundRevocation(Faults.BoundRequest(journey.NewEmail).Id);
        gate.Release();
        (await FinishAsync(request)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await AssertCredentialInertAsync(journey);
        (await StateAsync(journey.Owner.Id)).ShouldBe(before);
    }

    public enum WitnessDamage { Missing, WrongPurpose, WrongNonce }

    private async Task DamageCommittedWitnessAsync(RequestJourney journey, WitnessDamage damage)
    {
        var row = (await RequestRowsAsync(journey)).ShouldHaveSingleItem();
        await using var scope = factory.Services.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var selected = app.AuditLogEntries.Where(entry => entry.Id == row.Id);
        // Operator damage is unreachable from current writers. Only the read side's safe refusal is asserted.
        var changed = damage switch
        {
            WitnessDamage.Missing => await selected.ExecuteDeleteAsync(Ct),
            WitnessDamage.WrongPurpose => await selected.ExecuteUpdateAsync(update =>
                update.SetProperty(entry => entry.EventType, RequestAccountEmailChangeCommand.RequestedEventType), Ct),
            WitnessDamage.WrongNonce => await selected.ExecuteUpdateAsync(update =>
                update.SetProperty(entry => entry.Payload, JsonSerializer.Serialize(new { requestId = ChallengeId.Generate().Reveal() })), Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };
        changed.ShouldBe(1);
    }

    [Theory]
    [InlineData(WitnessDamage.Missing)]
    [InlineData(WitnessDamage.WrongPurpose)]
    [InlineData(WitnessDamage.WrongNonce)]
    public async Task Verify_ShouldDegradeSafelyWithoutConsumingTheCode_WhenOperatorDamageBreaksTheExactWitness(WitnessDamage damage)
    {
        var journey = await JourneyAsync(admin: false);
        (await RequestAsync(journey)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await AssertExactWitnessAsync(journey);
        await DamageCommittedWitnessAsync(journey, damage);
        await RefusesGrantAsync(await VerifyAsync(journey, SelfCode(journey)));
        Faults.Consumptions(Faults.BoundRequest(journey.NewEmail).Id).ShouldBe(0);
        (await BoundAttemptsAsync(Faults.BoundRequest(journey.NewEmail).Id)).ToString().ShouldBe("0");
        (await StateAsync(journey.Owner.Id)).Email.ShouldBe(journey.Owner.Email);
    }

    [Theory]
    [InlineData(WitnessDamage.Missing)]
    [InlineData(WitnessDamage.WrongPurpose)]
    [InlineData(WitnessDamage.WrongNonce)]
    public async Task Confirm_ShouldDegradeSafelyWithoutMovingTheAddress_WhenOperatorDamageBreaksADerivedGrantsWitness(WitnessDamage damage)
    {
        var journey = await JourneyAsync(admin: false);
        (await RequestAsync(journey)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var verified = await VerifyAsync(journey, SelfCode(journey));
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var grant = (await verified.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changeEmailGrant")
            .GetString().ShouldNotBeNull();
        var before = await StateAsync(journey.Owner.Id);
        await DamageCommittedWitnessAsync(journey, damage);
        await RefusesGrantAsync(await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, journey.Owner.Session,
            grant, journey.NewEmail, Ct));
        (await StateAsync(journey.Owner.Id)).ShouldBe(before);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.CountAsync(row =>
            row.AggregateId == journey.Owner.Id && row.EventType == "User.EmailChanged", Ct)).ShouldBe(0);
    }

    private async Task LetSelfCooldownsExpireAsync(Owner owner)
    {
        await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, owner.Email, Ct);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        // The clock expires the actual user's request cooldown, leaving its proof and daily budget intact.
        (await redis.GetDatabase().KeyDeleteAsync(RedisRateBudget.Key(
            ChangeEmailPolicy.UserCooldown(TimeSpan.FromSeconds(60)), owner.Id.ToString()))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldNotReviveOrRevokeAReplacement_WhenOlderAcceptedTransportFinishesLate(bool admin)
    {
        var original = await JourneyAsync(admin);
        using var gate = Faults.PauseTransport(original.NewEmail);
        var oldRequest = RequestAsync(original);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        RequestJourney replacement;
        if (admin)
            replacement = await JourneyAsync(admin: true, original.Owner);
        else
        {
            await LetSelfCooldownsExpireAsync(original.Owner);
            replacement = await JourneyAsync(admin: false, original.Owner);
        }
        try
        {
            (await RequestAsync(replacement).WaitAsync(Deadline, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            (await RequestRowsAsync(original)).Count.ShouldBe(1);
        }
        finally
        {
            gate.Release();
        }
        (await FinishAsync(oldRequest)).IsSuccessStatusCode.ShouldBeFalse();
        await AssertExactWitnessAsync(replacement);
        if (admin)
        {
            await AgeAdminRecordAsync(replacement);
            var code = ReauthTestHelpers.MailsTo(factory, replacement.NewEmail)[^1].Content
                .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>().Code.Reveal();
            (await AccountEmailChangeKit.CompleteAsync(_client, replacement.Owner.Email, replacement.NewEmail, code, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        else
        {
            await RefusesGrantAsync(await VerifyAsync(original, SelfCode(original)));
            (await VerifyAsync(replacement, SelfCode(replacement))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldRemainCancelled_WhenExactCleanupRunsWhileMailWaits(bool admin)
    {
        var journey = await JourneyAsync(admin);
        using var gate = Faults.PauseTransport(journey.NewEmail);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        try
        {
            if (admin)
            {
                (await journey.Administrator!.Client.DeleteAsync(AccountEmailChangeKit.Path(journey.Owner.Id), Ct)
                    .WaitAsync(Deadline, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }
            else
            {
                var record = Faults.BoundRequest(journey.NewEmail);
                await using var scope = factory.Services.CreateAsyncScope();
                var challenges = scope.ServiceProvider.GetRequiredService<ILoginChallengeStore>();
                // The request's exact cleanup is the production actor that revokes this purpose-bound record.
                await challenges.RevokeBoundAsync(record.Id, record.Binding, Ct);
                (await challenges.ReadEmailChangeRequestAsync(record.Id, journey.Owner.Id, Ct)).ShouldBeNull();
            }
        }
        finally
        {
            gate.Release();
        }
        (await FinishAsync(request)).IsSuccessStatusCode.ShouldBeFalse();
        (await RequestRowsAsync(journey)).ShouldBeEmpty();
        (await HasPhysicalCredentialAsync(journey)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldNotRenewAnExpiredCredential_WhenAcceptedTransportFinishesAfterItsLifetime(bool admin)
    {
        var journey = await JourneyAsync(admin);
        using var gate = Faults.PauseTransport(journey.NewEmail);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        try
        {
            await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
            var key = admin
                ? RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(journey.NewEmail))
                : RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(Faults.BoundRequest(journey.NewEmail).Id));
            // The clock's Redis TTL expiry removes the actual record, without rewriting any original proof.
            (await redis.GetDatabase().KeyExpireAsync(key, TimeSpan.FromMilliseconds(1))).ShouldBeTrue();
            await Task.Delay(50, Ct);
            (await redis.GetDatabase().KeyExistsAsync(key)).ShouldBeFalse();
        }
        finally
        {
            gate.Release();
        }
        (await FinishAsync(request)).IsSuccessStatusCode.ShouldBeFalse();
        (await RequestRowsAsync(journey)).ShouldBeEmpty();
        (await HasPhysicalCredentialAsync(journey)).ShouldBeFalse();
        ReauthTestHelpers.MailsTo(factory, journey.NewEmail).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Request_ShouldRejectOriginalAuthorityPermanently_WhenActorOrTargetTransitionsWhileMailWaits(bool admin, bool actor)
    {
        var journey = await JourneyAsync(admin);
        var other = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var firstGrant = await ReauthTestHelpers.MintGrantAsync(factory, other.Client, other.SessionId, other.Email, Ct);
        var changedId = actor ? journey.Administrator!.UserId : journey.Owner.Id;
        var before = await StateAsync(changedId);
        using var gate = Faults.PauseTransport(journey.NewEmail);
        var request = RequestAsync(journey);
        await gate.Accepted.Task.WaitAsync(Deadline, Ct);
        try
        {
            (await TransitionAsync(other, changedId, "suspend", firstGrant).WaitAsync(Deadline, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, other.Email, Ct);
            (await TransitionAsync(other, changedId, "reinstate").WaitAsync(Deadline, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            (await StateAsync(changedId)).AccessRevision.ShouldBe(before.AccessRevision + 2);
        }
        finally
        {
            gate.Release();
        }
        (await FinishAsync(request)).IsSuccessStatusCode.ShouldBeFalse();
        (await RequestRowsAsync(journey)).ShouldBeEmpty();
        (await StateAsync(journey.Owner.Id)).Email.ShouldBe(journey.Owner.Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_ShouldReportUnknownWithoutReplayingMail_WhenTheActualActivationCommitAcknowledgementIsLost(bool admin)
    {
        var journey = await JourneyAsync(admin);
        using var loss = Faults.CommitAcknowledgementLoss.AfterCommit(journey.EventType, journey.Owner.Id);
        var response = await RequestAsync(journey);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("title").GetString().ShouldBe("Admin.AccountAccessOutcomeUnknown");
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        body.TryGetProperty("challengeId", out _).ShouldBeFalse();
        Faults.CommitAcknowledgementLoss.Fired.ShouldBe(1);
        await AssertExactWitnessAsync(journey);
        ReauthTestHelpers.MailsTo(factory, journey.NewEmail).Count.ShouldBe(1);
        (await StateAsync(journey.Owner.Id)).Email.ShouldBe(journey.Owner.Email);
        // Exact primary evidence, rather than a status guess, admits the already-committed original request.
        if (admin)
        {
            await AgeAdminRecordAsync(journey);
            var code = ReauthTestHelpers.MailsTo(factory, journey.NewEmail)[^1].Content
                .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>().Code.Reveal();
            (await AccountEmailChangeKit.CompleteAsync(_client, journey.Owner.Email, journey.NewEmail, code, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        else
            (await VerifyAsync(journey, SelfCode(journey))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_ShouldPreserveItsOriginalMetadataThroughTheDerivedGrant_WhenActivationCommits()
    {
        var journey = await JourneyAsync(admin: false);
        var original = await StateAsync(journey.Owner.Id);
        (await RequestAsync(journey)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await AssertExactWitnessAsync(journey);
        var bound = Faults.BoundRequest(journey.NewEmail);
        var request = bound.EmailChangeRequest.ShouldNotBeNull();
        request.IsValid.ShouldBeTrue();
        request.RequestId.ShouldBe(bound.Id.Reveal());
        bound.Access.UserId.ShouldBe(journey.Owner.Id);
        bound.Access.AccessRevision.ShouldBe(original.AccessRevision);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var boundKey = RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(bound.Id));
        (await db.KeyExistsAsync(boundKey.Replace("/v2/", "/v1/", StringComparison.Ordinal))).ShouldBeFalse();
        var protectors = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var originalPayload = JsonSerializer.Deserialize<JsonElement>(protectors
            .CreateProtector(RedisLoginChallengeStore.ProtectorPurpose).CreateProtector("3")
            .Unprotect((byte[])(await db.HashGetAsync(boundKey, "p"))!));
        originalPayload.GetProperty("q").Deserialize<EmailChangeRequestProof>().ShouldBe(request);

        var verified = await VerifyAsync(journey, SelfCode(journey));
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = GrantToken.FromRaw((await verified.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("changeEmailGrant").GetString().ShouldNotBeNull());
        var grantKey = RedisGrantStore.Key(token);
        (await db.KeyExistsAsync(grantKey.Replace("/v2/", "/v1/", StringComparison.Ordinal))).ShouldBeFalse();
        var derived = JsonSerializer.Deserialize<JsonElement>(protectors
            .CreateProtector(RedisGrantStore.ProtectorPurpose).CreateProtector("3")
            .Unprotect((byte[])(await db.StringGetAsync(grantKey))!));
        derived.GetProperty("q").Deserialize<EmailChangeRequestProof>().ShouldBe(request);
        derived.GetProperty("g").GetRawText().ShouldBe(originalPayload.GetProperty("g").GetRawText());
        derived.GetProperty("u").GetGuid().ShouldBe(journey.Owner.Id);
        derived.GetProperty("e").GetString().ShouldBe(journey.NewEmail);
        (await StateAsync(journey.Owner.Id)).ShouldBe(original);
    }

    private async Task AddHistoricalRequestAuditAsync(Owner owner)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var held = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([owner.Id], false, Ct);
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // AuditBehavior at 22aefd8db wrote this event with no nonce for ChangeEmailCommand's success.
        var row = AuditLogEntry.Create(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow,
            Guid.NewGuid(), owner.Id, ChangeEmailCommand.RequestedEventType, "User", owner.Id, null, null);
        row.Payload.ShouldBeNull();
        app.AuditLogEntries.Add(row);
        await app.SaveChangesAsync(Ct);
        await held.CommitAsync(Ct);
    }

    [Fact]
    public async Task Verify_ShouldRequireAFreshRequest_WhenAGenuinePreWitnessV1ChangeEmailChallengeSurvivesDeployment()
    {
        var owner = await OwnerAsync();
        var before = await StateAsync(owner.Id);
        before.AccessRevision.ShouldBe(0);
        before.CredentialCutoff.ShouldBe(0);
        await AddHistoricalRequestAuditAsync(owner);
        var challenge = ChallengeId.Generate();
        var code = ChallengeCodeArm.Mint();
        var next = Address();
        var binding = new ChallengeBinding(ChallengePurpose.ChangeEmail, owner.Id);
        var segment = RedisLoginChallengeStore.RecordSegment(challenge);
        var key = RedisLoginChallengeStore.BoundRecordKey(segment).Replace("/v2/", "/v1/", StringComparison.Ordinal);
        var index = RedisLoginChallengeStore.BoundIndexKey(binding).Replace("/v2/", "/v1/", StringComparison.Ordinal);
        // RedisLoginChallengeStore.PutBoundAsync at 22aefd8db: unpadded {e,c,u}, v1 protector and index.
        // Request_ShouldPreserveItsOriginalMetadataThroughTheDerivedGrant_WhenActivationCommits pins today's q+g writer.
        var payload = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisLoginChallengeStore.LegacyProtectorPurpose).CreateProtector("3")
            .Protect(JsonSerializer.SerializeToUtf8Bytes(new { e = next, c = code.Reveal(), u = owner.Id }));
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var transaction = redis.GetDatabase().CreateTransaction();
        var write = transaction.HashSetAsync(key, [new HashEntry("p", payload), new HashEntry("a", 0)]);
        var expiry = transaction.KeyExpireAsync(key, LoginChallengePolicy.ChallengeTtl);
        var pointer = transaction.StringSetAsync(index, segment, LoginChallengePolicy.ChallengeTtl);
        (await transaction.ExecuteAsync()).ShouldBeTrue();
        await Task.WhenAll(write, expiry, pointer);

        await RefusesGrantAsync(await ReauthTestHelpers.VerifyAddressChangeAsync(_client, owner.Session,
            challenge.Reveal(), code.Reveal(), Ct));
        (await redis.GetDatabase().HashGetAsync(key, "a")).ToString().ShouldBe("0");
        Faults.Consumptions(challenge).ShouldBe(0);
        (await StateAsync(owner.Id)).ShouldBe(before);
    }

    [Fact]
    public async Task Confirm_ShouldRequireAFreshRequest_WhenAGenuinePreWitnessV1ChangeEmailGrantSurvivesDeployment()
    {
        var owner = await OwnerAsync();
        var before = await StateAsync(owner.Id);
        before.AccessRevision.ShouldBe(0);
        before.CredentialCutoff.ShouldBe(0);
        await AddHistoricalRequestAuditAsync(owner);
        var next = Address();
        var token = GrantToken.Generate();
        // RedisGrantStore.IssueAsync at 22aefd8db: purpose 3 {p,e,u}, padded to that build's shared ceiling.
        // Request_ShouldPreserveItsOriginalMetadataThroughTheDerivedGrant_WhenActivationCommits pins today's q+g writer.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { p = 3, e = next, u = owner.Id });
        var ceiling = JsonSerializer.SerializeToUtf8Bytes(new
        {
            p = 4,
            e = new string('"', EmailAddressRules.MaximumLength),
            u = Guid.Empty,
            pr = new string('"', ExternalProviderKey.MaximumLength),
            s = new string('"', ExternalSubject.MaximumLength),
        }).Length;
        var padded = new byte[ceiling];
        body.CopyTo(padded, 0);
        padded.AsSpan(body.Length).Fill((byte)' ');
        var payload = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisGrantStore.LegacyProtectorPurpose).CreateProtector("3").Protect(padded);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var key = RedisGrantStore.Key(token).Replace("/v2/", "/v1/", StringComparison.Ordinal);
        (await redis.GetDatabase().StringSetAsync(key, payload, LoginChallengePolicy.GrantTtl, When.NotExists))
            .ShouldBeTrue();

        await RefusesGrantAsync(await ReauthTestHelpers.ConfirmAddressChangeAsync(_client, owner.Session,
            token.Reveal(), next, Ct));
        (await StateAsync(owner.Id)).ShouldBe(before);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.CountAsync(row =>
            row.AggregateId == owner.Id && row.EventType == "User.EmailChanged", Ct)).ShouldBe(0);
    }

    // Retired /auth/register + NullPasswordHashes left confirmed=false/password-free accounts.
    // AccountRegistrationAtomicityTests.OpenAsync_ShouldCommitIdentityProfileAndAudit_WhenRegistrationSucceeds
    // pins that today's AccountRegistrar/UserAccountService creation always confirms the address.
    private async Task<Owner> HistoricalUnconfirmedOwnerAsync()
    {
        var email = Address();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = false };
        await using var scope = factory.Services.CreateAsyncScope();
        await using (var held = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([user.Id], false, Ct))
        {
            (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user))
                .Succeeded.ShouldBeTrue();
            await held.CommitAsync(Ct);
        }
        var session = await AuthTestHelpers.RegisterJobSeekerAndCreateSessionAsync(scope.ServiceProvider,
            user.Id, SessionLifetime.Persistent, Ct);
        (await StateAsync(user.Id)).InboxConfirmed.ShouldBeFalse();
        return new Owner(user.Id, email, session);
    }

    private sealed record LoginPresentation(string ChallengeId, LoginChallengeEmail.CodeAndLink Mail);

    private async Task<LoginPresentation> LoginChallengeAsync(string email)
    {
        var before = ReauthTestHelpers.MailsTo(factory, email).Count;
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("challengeId").GetString()!;
        var elapsed = Stopwatch.StartNew();
        while (ReauthTestHelpers.MailsTo(factory, email).Count == before)
        {
            elapsed.Elapsed.ShouldBeLessThan(Deadline);
            await Task.Delay(25, Ct);
        }
        return new LoginPresentation(id,
            ReauthTestHelpers.MailsTo(factory, email)[^1].Content.ShouldBeOfType<LoginChallengeEmail.CodeAndLink>());
    }

    private Task<HttpResponseMessage> ProveLoginAsync(LoginPresentation challenge) => _client.PostAsJsonAsync(
        "/api/v1/auth/challenge/verify", new { challengeId = challenge.ChallengeId, code = challenge.Mail.Code.Reveal() }, Ct);

    private async Task LetLoginCooldownExpireAsync(string email)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        // The clock expires only the actual request cooldown; its original proof is unchanged.
        (await redis.GetDatabase().KeyDeleteAsync(RedisRateBudget.Key(
            LoginChallengePolicy.Cooldown(TimeSpan.FromSeconds(60)), email))).ShouldBeTrue();
    }

    private async Task<int> InboxProofRowsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.CountAsync(row =>
            row.AggregateId == userId && row.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType, Ct);
    }

    private static async Task<string> AssertSignedInAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().ShouldBe("signedIn");
        return body.GetProperty("sessionId").GetString().ShouldNotBeNull();
    }

    private static async Task AssertNoLoginAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.TryGetProperty("sessionId", out _).ShouldBeFalse();
        body.TryGetProperty("grantToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task FirstProof_ShouldRefuseAnOlderOriginalGeneration_WhenAConcurrentProofConfirmsAfterItsFalseHint()
    {
        var owner = await HistoricalUnconfirmedOwnerAsync();
        var original = await LoginChallengeAsync(owner.Email);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeAdmission(owner.Id);
        var first = ProveLoginAsync(original);
        await gate.Reached.Task.WaitAsync(Deadline, Ct);
        var before = await StateAsync(owner.Id);
        try
        {
            gate.LifecycleRequested.ShouldBe(true);
            before.InboxConfirmed.ShouldBeFalse();
            await LetLoginCooldownExpireAsync(owner.Email);
            var fresh = await LoginChallengeAsync(owner.Email);
            await AssertSignedInAsync(await ProveLoginAsync(fresh).WaitAsync(Deadline, Ct));
            (await StateAsync(owner.Id)).AccessRevision.ShouldBe(before.AccessRevision + 1);
        }
        finally
        {
            gate.Release();
        }
        await AssertNoLoginAsync(await FinishAsync(first));
        (await InboxProofRowsAsync(owner.Id)).ShouldBe(1);
        (await StateAsync(owner.Id)).AccessRevision.ShouldBe(before.AccessRevision + 1);
    }

    [Fact]
    public async Task FirstProof_ShouldAdvanceGenerationOnce_WhenTwoIndependentlyConsumedProofsRunConcurrently()
    {
        var owner = await HistoricalUnconfirmedOwnerAsync();
        var before = await StateAsync(owner.Id);
        var firstChallenge = await LoginChallengeAsync(owner.Email);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeAdmission(owner.Id);
        var first = ProveLoginAsync(firstChallenge);
        await gate.Reached.Task.WaitAsync(Deadline, Ct);
        string? secondSession = null;
        try
        {
            await LetLoginCooldownExpireAsync(owner.Email);
            var secondChallenge = await LoginChallengeAsync(owner.Email);
            secondSession = await AssertSignedInAsync(await ProveLoginAsync(secondChallenge).WaitAsync(Deadline, Ct));
        }
        finally
        {
            gate.Release();
        }
        await AssertNoLoginAsync(await FinishAsync(first));
        secondSession.ShouldNotBeNull();
        (await InboxProofRowsAsync(owner.Id)).ShouldBe(1);
        var after = await StateAsync(owner.Id);
        after.AccessRevision.ShouldBe(before.AccessRevision + 1);
        after.InboxConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_ShouldDegradeSafelyBeforeAnyWrite_WhenOperatorDamageReversesAConfirmedHint()
    {
        var owner = await OwnerAsync();
        var challenge = await LoginChallengeAsync(owner.Email);
        var before = await StateAsync(owner.Id);
        using var gate = factory.AccountAccessFlowGates.PauseBeforeAdmission(owner.Id);
        var proof = ProveLoginAsync(challenge);
        await gate.Reached.Task.WaitAsync(Deadline, Ct);
        try
        {
            gate.LifecycleRequested.ShouldBe(false);
            // Unreachable operator damage: current Identity writers never change true confirmation to false.
            // Assert only the read side's refusal, never claim this is a production transition.
            await using var scope = factory.Services.CreateAsyncScope();
            await using var held = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
                .BeginAsync([owner.Id], true, Ct);
            (await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.Where(user => user.Id == owner.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.EmailConfirmed, false), Ct)).ShouldBe(1);
            await held.CommitAsync(Ct);
        }
        finally
        {
            gate.Release();
        }
        await AssertNoLoginAsync(await FinishAsync(proof));
        (await InboxProofRowsAsync(owner.Id)).ShouldBe(0);
        var damaged = await StateAsync(owner.Id);
        damaged.InboxConfirmed.ShouldBeFalse();
        damaged.AccessRevision.ShouldBe(before.AccessRevision);
        damaged.CredentialCutoff.ShouldBe(before.CredentialCutoff);
    }
}
