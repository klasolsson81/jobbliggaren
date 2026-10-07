using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public sealed class AccountAccessAddressChangeFencesTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _public = factory.CreateClient();
    private readonly string _token = AdminAccountsKit.NewToken();
    private string Address(string label) => AdminAccountsKit.Address(_token, label);

    private async Task<Guid> OwnerAsync() => await AdminAccountsKit.OpenActiveAsync(factory, Address("owner"), Ct);

    private async Task<AccountAccessChanged> TransitionAsync(AccountEmailChangeKit.Admin admin, Guid target, bool suspend, bool repeat = false)
    {
        if (repeat)
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        var response = await admin.Client.PostAsJsonAsync(
            $"/api/v1/admin/accounts/{target}/{(suspend ? "suspend" : "reinstate")}", new { reauthGrant = grant }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AccountAccessChanged>(Ct)).ShouldNotBeNull();
    }

    private async Task ShouldKeepOriginalAddressAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        var user = await identity.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new { user.Email, user.UserName }).SingleAsync(Ct);
        user.Email.ShouldBe(Address("owner"));
        user.UserName.ShouldBe(Address("owner"));
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries
            .AnyAsync(row => row.AggregateId == userId && row.EventType == "User.EmailChangedViaAdministrator", Ct))
            .ShouldBeFalse();
    }

    private Task<HttpResponseMessage> CompleteAsync(string newAddress, string code) =>
        AccountEmailChangeKit.CompleteAsync(_public, Address("owner"), newAddress, code, Ct);

    [Fact]
    public async Task PendingChange_ShouldRemainPermanentlyDead_WhenAccountIsSuspendedAndReinstated()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var next = Address("next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);

        await TransitionAsync(admin, owner, true);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await TransitionAsync(admin, owner, false, repeat: true);

        (await CompleteAsync(next, pending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    [Fact]
    public async Task PendingChange_ShouldHideAndStayUnusableAfterReinstate_WhenRedisCancellationFailed()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var next = Address("failed-cleanup-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);
        using (factory.AccountEmailChangeStoreFaults.FailingCancellation(owner))
        {
            await TransitionAsync(admin, owner, true);
            await TransitionAsync(admin, owner, false, repeat: true);
        }
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var key = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(next));
        (await redis.GetDatabase().KeyExistsAsync(key)).ShouldBeTrue();

        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CompleteAsync(next, pending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    [Fact]
    public async Task ConsumedProof_ShouldKeepItsOriginalRevision_WhenSuspensionFinishesBeforeProtectedWrite()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var next = Address("consumed-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);
        using var gate = factory.AccountEmailChangeStoreFaults.PauseAfterConsume(owner);
        var completion = CompleteAsync(next, pending.Code.Reveal());
        var proof = await gate.Consumed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        proof.Access.UserId.ShouldBe(owner);
        proof.Access.AccessRevision.ShouldBe(0);
        proof.RequestId.ShouldBe(pending.RequestId);

        await TransitionAsync(admin, owner, true);
        await TransitionAsync(admin, owner, false, repeat: true);
        gate.Release();

        (await completion.WaitAsync(TimeSpan.FromSeconds(30), Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    [Fact]
    public async Task DelayedCleanup_ShouldPreserveNewerPendingChangeAndSession_WhenReinstatedAccountStartsAgain()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, Address("old-next"), Address("owner"), Ct);
        var suspended = await TransitionAsync(admin, owner, true);
        await TransitionAsync(admin, owner, false, repeat: true);
        var freshAddress = Address("fresh-next");
        var freshPending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, freshAddress, Address("owner"), Ct);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountAccessReader>();
            var state = (await reader.ReadAsync(owner, Ct)).ShouldNotBeNull();
            state.AccessRevision.ShouldBe(2);
            var proof = new AccountAccessProof(await reader.ReadEpochAsync(Ct), owner, state.AccessRevision);
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            var fresh = (await sessions.CreateAsync(owner, proof, SessionLifetime.Persistent, Ct)).ShouldNotBeNull();
            await scope.ServiceProvider.GetRequiredService<IAccountAccessCleanup>()
                .CompleteAsync(suspended, Ct);
            (await sessions.GetAsync(fresh.Id, Ct)).ShouldNotBeNull().AccessRevision.ShouldBe(2);
        }

        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CompleteAsync(freshAddress, freshPending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RolledBackRequest_ShouldLeaveNoUsableAddressProof_WhenBothMailsWereAcceptedAndRevocationFails()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var next = Address("rollback-next");
        HttpResponseMessage refused;
        using (factory.AuditRowSaveFailure.FailingFor(RequestAccountEmailChangeCommand.RequestedEventType, admin.UserId))
        using (factory.AccountEmailChangeStoreFaults.FailingRevocation())
            refused = await AccountEmailChangeKit.RequestAsync(factory, admin, owner, next, Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var mailed = factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == next).Content
            .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>();
        factory.Emails.AccountEmailChangeNotices.ShouldContain(notice => notice.ToEmail == Address("owner"));
        (await AccountEmailChangeKit.AuditRowsAsync(factory, owner, Ct)).ShouldBeEmpty();
        await AgeUncommittedRecordAsync(next, 73);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CompleteAsync(next, mailed.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    // Only the clock moves this reachable, uncommitted pending record; it cannot fabricate a committed witness.
    private async Task<Guid> AgeUncommittedRecordAsync(string email, int hours)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var key = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(email));
        var value = await db.HashGetAsync(key, "p");
        value.IsNull.ShouldBeFalse();
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisAccountEmailChangeStore.ProtectorPurpose);
        var original = JsonSerializer.Deserialize<RedisAccountEmailChangeStore.ChangePayload>(protector.Unprotect((byte[])value!))
            .ShouldNotBeNull();
        var milliseconds = checked((long)TimeSpan.FromHours(hours).TotalMilliseconds);
        var shifted = original with { NotBefore = original.NotBefore - milliseconds, ExpiresAt = original.ExpiresAt - milliseconds };
        (shifted with { NotBefore = original.NotBefore, ExpiresAt = original.ExpiresAt }).ShouldBe(original);
        await db.HashSetAsync(key,
        [
            new HashEntry("p", protector.Protect(JsonSerializer.SerializeToUtf8Bytes(shifted))),
            new HashEntry("n", shifted.NotBefore), new HashEntry("x", shifted.ExpiresAt),
        ]);
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(shifted.ExpiresAt);
        var issuedAt = expiresAt - AccountEmailChangePolicy.Ttl;
        DateTimeOffset.FromUnixTimeMilliseconds(shifted.NotBefore).ShouldBe(issuedAt + AccountEmailChangePolicy.Delay);
        var remaining = expiresAt - factory.Services.GetRequiredService<IDateTimeProvider>().UtcNow;
        remaining.ShouldBeGreaterThan(TimeSpan.Zero);
        foreach (var pendingKey in new[] { key, RedisAccountEmailChangeStore.IndexKey(original.UserId) })
            (await db.KeyExpireAsync(pendingKey, TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)))).ShouldBeTrue();
        return original.RequestId.ShouldNotBeNull();
    }

    [Fact]
    public async Task RolledBackRequest_ShouldNotBorrowAnotherNonce_WhenTheSameTargetHasAnOlderCommittedRequest()
    {
        var owner = await OwnerAsync();
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var prior = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, Address("prior-next"), Address("owner"), Ct);
        (await admin.Client.DeleteAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var next = Address("uncommitted-next");
        HttpResponseMessage refused;
        using (factory.AuditRowSaveFailure.FailingFor(RequestAccountEmailChangeCommand.RequestedEventType, admin.UserId))
        using (factory.AccountEmailChangeStoreFaults.FailingRevocation())
            refused = await AccountEmailChangeKit.RequestAsync(factory, admin, owner, next, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var mailed = factory.Emails.LoginChallenges.Last(mail => mail.ToEmail == next).Content
            .ShouldBeOfType<LoginChallengeEmail.AccountEmailChangeCode>();
        var uncommittedNonce = await AgeUncommittedRecordAsync(next, 73);
        uncommittedNonce.ShouldNotBe(prior.RequestId);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var requests = (await AccountEmailChangeKit.AuditRowsAsync(factory, owner, Ct))
            .Where(row => row.EventType == RequestAccountEmailChangeCommand.RequestedEventType).ToArray();
        requests.ShouldHaveSingleItem();
        JsonDocument.Parse(requests[0].Payload!).RootElement.GetProperty("requestId").GetGuid().ShouldBe(prior.RequestId);

        (await CompleteAsync(next, mailed.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    [Fact]
    public async Task CommittedWitness_ShouldRemainUsable_WhenTheProductionEraserAnonymizesItsActor()
    {
        var owner = await OwnerAsync();
        var next = Address("anonymized-actor-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var witness = (await app.AuditLogEntries.AsNoTracking()
                .Where(row => row.AggregateId == owner && row.EventType == RequestAccountEmailChangeCommand.RequestedEventType)
                .ToListAsync(Ct)).Single(row => JsonDocument.Parse(row.Payload!).RootElement.GetProperty("requestId")
                    .GetGuid() == pending.RequestId);
            var actor = witness.UserId.ShouldNotBeNull();
            // Art. 17's real transform clears the actor fields and preserves the exact authorization witness.
            (await scope.ServiceProvider.GetRequiredService<IAuditTrailEraser>().AnonymizeUserAuditTrailAsync(actor, Ct))
                .ShouldBeGreaterThan(0);
            var anonymized = await app.AuditLogEntries.AsNoTracking().SingleAsync(row => row.Id == witness.Id, Ct);
            anonymized.UserId.ShouldBeNull();
            anonymized.AggregateId.ShouldBe(witness.AggregateId);
            anonymized.AggregateType.ShouldBe(witness.AggregateType);
            anonymized.EventType.ShouldBe(witness.EventType);
            anonymized.Payload.ShouldBe(witness.Payload);
            anonymized.OccurredAt.ShouldBe(witness.OccurredAt);
            await using var access = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
                .BeginAsync([owner], false, Ct);
            (await scope.ServiceProvider.GetRequiredService<IAccountEmailChangeRequests>().HasCommittedRequestAsync(
                owner, pending.RequestId, pending.ExpiresAt - AccountEmailChangePolicy.Ttl, pending.ExpiresAt, Ct))
                .ShouldBeTrue();
            await access.CommitAsync(Ct);
        }

        (await CompleteAsync(next, pending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GenuineV1PendingProof_ShouldBeRefusedEvenWithOldAudit_WhenAdministratorMustRestartTheDelay()
    {
        var owner = await OwnerAsync();
        var next = Address("legacy-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var key = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(next));
        var index = RedisAccountEmailChangeStore.IndexKey(owner);
        var protectedValue = await db.HashGetAsync(key, "p");
        var keyring = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var current = keyring.CreateProtector(RedisAccountEmailChangeStore.ProtectorPurpose);
        var payload = JsonNode.Parse(current.Unprotect((byte[])protectedValue!)).ShouldNotBeNull();
        payload["q"].ShouldNotBeNull();
        payload["g"].ShouldNotBeNull();
        // The retired v1 writer emitted neither field. The HTTP request above pins today's v2 writer.
        payload.AsObject().Remove("q").ShouldBeTrue();
        payload.AsObject().Remove("g").ShouldBeTrue();
        var legacyKey = key.Replace("/v2/", "/v1/", StringComparison.Ordinal);
        var legacyIndex = index.Replace("/v2/", "/v1/", StringComparison.Ordinal);
        (await db.KeyRenameAsync(key, legacyKey)).ShouldBeTrue();
        (await db.KeyRenameAsync(index, legacyIndex)).ShouldBeTrue();
        await db.HashSetAsync(legacyKey, "p", keyring.CreateProtector(RedisAccountEmailChangeStore.LegacyProtectorPurpose)
            .Protect(JsonSerializer.SerializeToUtf8Bytes(payload)));

        var administrator = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        (await administrator.Client.GetAsync(AccountEmailChangeKit.Path(owner), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CompleteAsync(next, pending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }

    [Fact]
    public async Task MalformedV2PendingProof_ShouldFailClosed_WhenSecurityGenerationIsMissing()
    {
        var owner = await OwnerAsync();
        var next = Address("malformed-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner, next, Address("owner"), Ct);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(factory.VolatileRedisConnectionString);
        var db = redis.GetDatabase();
        var key = RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(next));
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(RedisAccountEmailChangeStore.ProtectorPurpose);
        var payload = JsonNode.Parse(protector.Unprotect((byte[])(await db.HashGetAsync(key, "p"))!)).ShouldNotBeNull();
        // Declared unreachable writer corruption: only safe read-side degradation is asserted.
        payload.AsObject().Remove("g").ShouldBeTrue();
        await db.HashSetAsync(key, "p", protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload)));

        (await CompleteAsync(next, pending.Code.Reveal())).StatusCode.ShouldBe(HttpStatusCode.Gone);
        await ShouldKeepOriginalAddressAsync(owner);
    }
}
