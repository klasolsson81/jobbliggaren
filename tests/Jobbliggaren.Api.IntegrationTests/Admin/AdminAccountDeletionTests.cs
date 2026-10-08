using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

[Collection("Api")]
public sealed class AdminAccountDeletionTests(ApiFactory factory)
{
    private readonly HashSet<Guid> _reauthenticated = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Path(Guid id) => $"/api/v1/admin/accounts/{id}/deletion";

    private Task<AccountEmailChangeKit.Admin> AdminAsync() =>
        AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

    private async Task<string> GrantAsync(AccountEmailChangeKit.Admin admin)
    {
        if (!_reauthenticated.Add(admin.UserId))
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        return await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
    }

    private async Task<HttpResponseMessage> ScheduleAsync(AccountEmailChangeKit.Admin admin, Guid target) =>
        await admin.Client.PostAsJsonAsync(Path(target), new { reauthGrant = await GrantAsync(admin) }, Ct);

    private async Task<(Guid UserId, string Email, string SessionId)> OwnerAsync()
    {
        var email = AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "owner");
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return (await AdminAccountsKit.UserIdAsync(factory, email, Ct), email, session);
    }

    private async Task<AccountAccessSnapshot> AccessAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(userId, Ct))
            .ShouldNotBeNull();
    }

    private async Task LinkEveryKnownProviderAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>()
            .BeginAsync([userId], lifecycle: false, Ct);
        var writer = scope.ServiceProvider.GetRequiredService<IExternalLoginWriter>();
        foreach (var provider in ExternalProviderKey.Known)
            (await writer.LinkAsync(userId, provider, ExternalSubject.TryCreate(Guid.NewGuid().ToString("N"))!.Value, Ct))
                .ShouldBe(ExternalLinkResult.Linked);
        await transaction.CommitAsync(Ct);
        (await ProviderCountAsync(userId)).ShouldBe(ExternalProviderKey.Known.Count);
    }

    private async Task<int> ProviderCountAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().UserLogins
            .CountAsync(link => link.UserId == userId, Ct);
    }

    private async Task<(string? Security, string? Concurrency)> IdentityStampsAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var stamps = await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == userId).Select(user => new { user.SecurityStamp, user.ConcurrencyStamp }).SingleAsync(Ct);
        return (stamps.SecurityStamp, stamps.ConcurrencyStamp);
    }

    private async Task<HttpStatusCode> MeAsync(string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await factory.CreateClient().SendAsync(request, Ct)).StatusCode;
    }

    private sealed record AuditRow(Guid? Actor, Guid Target, string? Payload);

    private async Task<IReadOnlyList<AuditRow>> AuditsAsync(Guid target)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == target && row.EventType == "Admin.AccountDeletionScheduled")
            .Select(row => new AuditRow(row.UserId, row.AggregateId, row.Payload)).ToListAsync(Ct);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString().ShouldBe(code);
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Schedule_ShouldRefuseAnonymousAndOrdinaryCaller_WhenAdminPolicyIsMissing()
    {
        var owner = await OwnerAsync();
        (await factory.CreateClient().PostAsJsonAsync(Path(owner.UserId), new { reauthGrant = "unusable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var ordinary = factory.CreateClient();
        ordinary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.SessionId);
        (await ordinary.PostAsJsonAsync(Path(owner.UserId), new { reauthGrant = "unusable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccessAsync(owner.UserId)).DeletedAt.ShouldBeNull();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectCommand_ShouldEnforceAuthorization_WhenEndpointPolicyIsBypassed(bool signedIn)
    {
        var owner = await OwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = signedIn
            ? new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = Ct }
            : null;
        try
        {
            if (signedIn)
            {
                var context = accessor.HttpContext.ShouldNotBeNull();
                context.Request.Headers.Authorization = $"Bearer {owner.SessionId}";
                var authenticated = await context.AuthenticateAsync("Bearer");
                authenticated.Succeeded.ShouldBeTrue();
                context.User = authenticated.Principal.ShouldNotBeNull();
                await Should.ThrowAsync<ForbiddenException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>()
                        .Send(new ScheduleAccountDeletionCommand(owner.UserId, "unusable"), Ct));
            }
            else
                await Should.ThrowAsync<UnauthorizedException>(async () =>
                    await scope.ServiceProvider.GetRequiredService<IMediator>()
                        .Send(new ScheduleAccountDeletionCommand(owner.UserId, "unusable"), Ct));
        }
        finally { accessor.HttpContext = previous; }
        (await AccessAsync(owner.UserId)).DeletedAt.ShouldBeNull();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Schedule_ShouldRequireOwnInboxProof_WhenAdministratorOnlyHasASession()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        (await admin.Client.PostAsJsonAsync(Path(owner.UserId), new { reauthGrant = "unusable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AccessAsync(owner.UserId)).DeletedAt.ShouldBeNull();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_ShouldFenceCorrectTargetAndReturnActualTiming_WhenActiveOrSuspended(bool suspended)
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var control = await OwnerAsync();
        await LinkEveryKnownProviderAsync(owner.UserId);
        await LinkEveryKnownProviderAsync(control.UserId);
        if (suspended)
            (await admin.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{owner.UserId}/suspend",
                new { reauthGrant = await GrantAsync(admin) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await AccessAsync(owner.UserId);
        var stampsBefore = await IdentityStampsAsync(owner.UserId);
        var response = await ScheduleAsync(admin, owner.UserId);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        receipt.GetProperty("userId").GetGuid().ShouldBe(owner.UserId);
        var after = await AccessAsync(owner.UserId);
        after.DeletedAt.ShouldBe(receipt.GetProperty("deletedAt").GetDateTimeOffset());
        after.IsSuspended.ShouldBe(suspended);
        after.AccessRevision.ShouldBe(before.AccessRevision + 1);
        after.CredentialCutoff.ShouldBeGreaterThan(before.CredentialCutoff);
        after.CanAuthenticate.ShouldBeFalse();
        var stampsAfter = await IdentityStampsAsync(owner.UserId);
        stampsAfter.Security.ShouldNotBe(stampsBefore.Security);
        stampsAfter.Concurrency.ShouldNotBe(stampsBefore.Concurrency);
        (await ProviderCountAsync(owner.UserId)).ShouldBe(0);
        (await ProviderCountAsync(control.UserId)).ShouldBe(ExternalProviderKey.Known.Count);
        var eligible = receipt.GetProperty("eligibleAt").GetDateTimeOffset();
        eligible.ShouldBe(after.DeletedAt.ShouldNotBeNull().AddDays(HardDeleteAccountsJob.RestoreWindowDays));
        var planned = receipt.GetProperty("scheduledRunAt").GetDateTimeOffset();
        planned.ShouldBeGreaterThan(eligible);
        planned.UtcDateTime.Hour.ShouldBe(4);
        planned.Minute.ShouldBe(0);
        planned.ShouldBe(AccountRestoreWindow.FirstScheduledRunAfter(eligible));
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(admin.SessionId)).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(control.SessionId)).ShouldBe(HttpStatusCode.OK);
        (await AccessAsync(control.UserId)).DeletedAt.ShouldBeNull();
        var audit = (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
        audit.Actor.ShouldBe(admin.UserId);
        audit.Target.ShouldBe(owner.UserId);
        audit.Payload.ShouldBeNull();
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

        var detail = await admin.Client.GetFromJsonAsync<JsonElement>(AdminAccountsKit.DetailPath(owner.UserId), Ct);
        detail.GetProperty("status").GetString().ShouldBe("PendingDeletion");
        detail.GetProperty("deletion").GetProperty("scheduledRunAt").GetDateTimeOffset().ShouldBe(planned);
        detail.GetProperty("deletionPreview").ValueKind.ShouldBe(JsonValueKind.Null);
        var search = await AdminAccountsKit.SearchOkAsync(admin.Client, new { address = owner.Email }, Ct);
        AdminAccountsKit.Items(search).ShouldHaveSingleItem().GetProperty("deletion")
            .GetProperty("scheduledRunAt").GetDateTimeOffset().ShouldBe(planned);
        search.GetProperty("counts").GetProperty("pendingDeletion").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Schedule_ShouldRefuseRepeatedRequestWithoutChanges_WhenDeletionIsAlreadyPending()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        (await ScheduleAsync(admin, owner.UserId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var before = await AccessAsync(owner.UserId);
        await ProblemAsync(await ScheduleAsync(admin, owner.UserId), HttpStatusCode.Conflict,
            AccountAccessErrors.AlreadyPendingDeletion);
        (await AccessAsync(owner.UserId)).ShouldBe(before);
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Schedule_ShouldRefuseSelfWithoutSuccessAudit_WhenOwnInboxIsProven()
    {
        var admin = await AdminAsync();
        var before = await AccessAsync(admin.UserId);
        await ProblemAsync(await ScheduleAsync(admin, admin.UserId), HttpStatusCode.Conflict, AccountAccessErrors.SelfDeletion);
        (await AccessAsync(admin.UserId)).ShouldBe(before);
        (await AuditsAsync(admin.UserId)).ShouldBeEmpty();
        (await MeAsync(admin.SessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Schedule_ShouldDistinguishMissingAccountFromRetiredRegistrationOrphan_WhenTargetIsUnavailable()
    {
        var admin = await AdminAsync();
        await ProblemAsync(await ScheduleAsync(admin, Guid.NewGuid()), HttpStatusCode.NotFound, AccountAccessErrors.AccountNotFound);
        // The helper names the retired writer and the current atomic-registration pin; no current orphan writer is assumed.
        var orphan = await AdminAccountsKit.CreateWithoutProfileAsync(factory,
            AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "historical-orphan"), Ct);
        await ProblemAsync(await ScheduleAsync(admin, orphan), HttpStatusCode.Gone, AccountAccessErrors.ProfileUnavailable);
        (await AuditsAsync(orphan)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Schedule_ShouldRollBackProfileRevisionAndAudit_WhenAuditPersistenceFails()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        await LinkEveryKnownProviderAsync(owner.UserId);
        var before = await AccessAsync(owner.UserId);
        var stampsBefore = await IdentityStampsAsync(owner.UserId);
        HttpResponseMessage response;
        using (factory.AuditRowSaveFailure.FailingFor("Admin.AccountDeletionScheduled", admin.UserId))
            response = await ScheduleAsync(admin, owner.UserId);
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await AccessAsync(owner.UserId)).ShouldBe(before);
        (await IdentityStampsAsync(owner.UserId)).ShouldBe(stampsBefore);
        (await ProviderCountAsync(owner.UserId)).ShouldBe(ExternalProviderKey.Known.Count);
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Schedule_ShouldReturnCommittedReceiptAndDenyOldSession_WhenRedisCleanupFails()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        HttpResponseMessage response;
        using (factory.SessionTeardownFaults.FailingFor(owner.UserId))
            response = await ScheduleAsync(admin, owner.UserId);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await AccessAsync(owner.UserId)).CanAuthenticate.ShouldBeFalse();
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_ShouldPermanentlyAbortPendingAddressChange_EvenWhenRedisCancellationFails(bool cleanupFailure)
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var next = AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "pending-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, next, owner.Email, Ct);
        using (cleanupFailure ? factory.AccountEmailChangeStoreFaults.FailingCancellation(owner.UserId) : null)
            (await ScheduleAsync(admin, owner.UserId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await admin.Client.GetAsync(AccountEmailChangeKit.Path(owner.UserId), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AccountEmailChangeKit.CompleteAsync(factory.CreateClient(), owner.Email, next, pending.Code.Reveal(), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Gone);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<Jobbliggaren.Infrastructure.Identity.AppIdentityDbContext>().Users.AsNoTracking()
            .Where(user => user.Id == owner.UserId).Select(user => user.Email).SingleAsync(Ct)).ShouldBe(owner.Email);
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Schedule_ShouldFenceAlreadyConsumedAddressProof_WhenDeletionWinsBeforeItsProtectedWrite()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var next = AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "consumed-next");
        var pending = await AccountEmailChangeKit.ChangeStartedHoursAgoAsync(factory, owner.UserId, next, owner.Email, Ct);
        using var gate = factory.AccountEmailChangeStoreFaults.PauseAfterConsume(owner.UserId);
        var completion = AccountEmailChangeKit.CompleteAsync(factory.CreateClient(), owner.Email, next, pending.Code.Reveal(), Ct);
        var proof = await gate.Consumed.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        proof.Access.UserId.ShouldBe(owner.UserId);
        (await ScheduleAsync(admin, owner.UserId)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        gate.Release();
        (await completion.WaitAsync(TimeSpan.FromSeconds(30), Ct)).StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await AccessAsync(owner.UserId)).Email.ShouldBe(owner.Email);
    }

    [Fact]
    public async Task Schedule_ShouldReportUnknownAndNeverReplay_WhenActualCommitAcknowledgementIsLost()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        HttpResponseMessage response;
        using (factory.CommitAcknowledgementLoss.AfterDeletionCommit(owner.UserId))
            response = await ScheduleAsync(admin, owner.UserId);
        await ProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Admin.AccountAccessOutcomeUnknown");
        factory.CommitAcknowledgementLoss.Fired.ShouldBe(1);
        var committed = await AccessAsync(owner.UserId);
        committed.DeletedAt.ShouldNotBeNull();
        committed.AccessRevision.ShouldBe(1);
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
        await ProblemAsync(await ScheduleAsync(admin, owner.UserId), HttpStatusCode.Conflict, AccountAccessErrors.AlreadyPendingDeletion);
        (await AccessAsync(owner.UserId)).ShouldBe(committed);
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ConcurrentDeletions_ShouldLeaveOneEffectiveActor_WhenAdministratorsTargetEachOther()
    {
        var first = await AdminAsync();
        var second = await AdminAsync();
        var firstGrant = await GrantAsync(first);
        var secondGrant = await GrantAsync(second);
        var responses = await Task.WhenAll(
            first.Client.PostAsJsonAsync(Path(second.UserId), new { reauthGrant = firstGrant }, Ct),
            second.Client.PostAsJsonAsync(Path(first.UserId), new { reauthGrant = secondGrant }, Ct));
        responses.Count(response => response.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(1);
        var states = new[] { await AccessAsync(first.UserId), await AccessAsync(second.UserId) };
        states.Count(state => state.IsEffectiveAdmin).ShouldBe(1);
        ((await AuditsAsync(first.UserId)).Count + (await AuditsAsync(second.UserId)).Count).ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentDeletionAndSuspension_ShouldKeepOneEffectiveActor_AndAuditOnlyTheCommittedWrite()
    {
        var deleting = await AdminAsync();
        var suspending = await AdminAsync();
        var deletionGrant = await GrantAsync(deleting);
        var suspensionGrant = await GrantAsync(suspending);
        var responses = await Task.WhenAll(
            deleting.Client.PostAsJsonAsync(Path(suspending.UserId), new { reauthGrant = deletionGrant }, Ct),
            suspending.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{deleting.UserId}/suspend",
                new { reauthGrant = suspensionGrant }, Ct));
        responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(1);
        responses.Count(response => response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK).ShouldBe(1);
        var states = new[] { await AccessAsync(deleting.UserId), await AccessAsync(suspending.UserId) };
        states.Count(state => state.IsEffectiveAdmin).ShouldBe(1);
        await using var scope = factory.Services.CreateAsyncScope();
        var committed = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => (row.AggregateId == deleting.UserId || row.AggregateId == suspending.UserId)
                && (row.EventType == "Admin.AccountDeletionScheduled" || row.EventType == "Admin.AccountSuspended"))
            .Select(row => new { row.UserId, row.AggregateId, row.EventType }).ToListAsync(Ct);
        committed.ShouldHaveSingleItem();
        committed[0].UserId.ShouldBe(committed[0].EventType == "Admin.AccountDeletionScheduled" ? deleting.UserId : suspending.UserId);
        committed[0].AggregateId.ShouldBe(committed[0].EventType == "Admin.AccountDeletionScheduled" ? suspending.UserId : deleting.UserId);
    }

    [Fact]
    public async Task Schedule_ShouldRefuseRevokedActorWithoutChangingTarget_WhenOldProofResumes()
    {
        var actor = await AdminAsync();
        var other = await AdminAsync();
        var owner = await OwnerAsync();
        var oldGrant = await GrantAsync(actor);
        using var gate = factory.AccountAccessFlowGates.PauseAfterReauthenticationProof(actor.UserId);
        var inFlight = actor.Client.PostAsJsonAsync(Path(owner.UserId), new { reauthGrant = oldGrant }, Ct);
        (await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldNotBeNull();
        (await other.Client.PostAsJsonAsync($"/api/v1/admin/accounts/{actor.UserId}/suspend",
            new { reauthGrant = await GrantAsync(other) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        gate.Release();
        (await inFlight.WaitAsync(TimeSpan.FromSeconds(30), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AccessAsync(owner.UserId)).DeletedAt.ShouldBeNull();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }
}
