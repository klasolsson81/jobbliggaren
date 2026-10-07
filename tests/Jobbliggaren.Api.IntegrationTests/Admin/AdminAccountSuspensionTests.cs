using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

[Collection("Api")]
public sealed class AdminAccountSuspensionTests(ApiFactory factory)
{
    private readonly HashSet<Guid> _reauthenticated = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Path(Guid id, bool suspend) =>
        $"/api/v1/admin/accounts/{id}/{(suspend ? "suspend" : "reinstate")}";

    private Task<AccountEmailChangeKit.Admin> AdminAsync() =>
        AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

    private async Task<HttpResponseMessage> ChangeAsync(AccountEmailChangeKit.Admin admin, Guid target, bool suspend)
    {
        if (!_reauthenticated.Add(admin.UserId))
            await ReauthTestHelpers.LetTheCooldownLapseAsync(factory, admin.Email, Ct);
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, admin.Client, admin.SessionId, admin.Email, Ct);
        return await admin.Client.PostAsJsonAsync(Path(target, suspend), new { reauthGrant = grant }, Ct);
    }

    private async Task<(Guid UserId, string Email, string SessionId)> OwnerAsync()
    {
        var email = AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "owner");
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        return (await AdminAccountsKit.UserIdAsync(factory, email, Ct), email, session);
    }

    private async Task<HttpStatusCode> MeAsync(string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return (await factory.CreateClient().SendAsync(request, Ct)).StatusCode;
    }

    private async Task<AccountAccessSnapshot> AccessAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(userId, Ct))
            .ShouldNotBeNull();
    }

    private async Task<IReadOnlyList<AuditRow>> AuditsAsync(Guid target)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == target
                && (row.EventType == "Admin.AccountSuspended" || row.EventType == "Admin.AccountReinstated"))
            .OrderBy(row => row.OccurredAt)
            .Select(row => new AuditRow(row.UserId, row.AggregateId, row.EventType, row.Payload))
            .ToListAsync(Ct);
    }

    private sealed record AuditRow(Guid? Actor, Guid Target, string EventType, string? Payload);

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("title").GetString().ShouldBe(code);
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAccess_ShouldRefuseAnonymousAndOrdinaryUsers_WhenAdminPolicyIsMissing(bool suspend)
    {
        var owner = await OwnerAsync();
        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(Path(owner.UserId, suspend), new { reauthGrant = "unusable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var ordinary = factory.CreateClient();
        ordinary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.SessionId);
        (await ordinary.PostAsJsonAsync(Path(owner.UserId, suspend), new { reauthGrant = "unusable" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AccessAsync(owner.UserId)).IsSuspended.ShouldBeFalse();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectCommand_ShouldRefuseAnonymousCaller_WhenHttpPolicyIsBypassed(bool suspend)
    {
        var owner = await OwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = null;
        try
        {
            await Should.ThrowAsync<UnauthorizedException>(async () =>
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                if (suspend)
                    await mediator.Send(new SuspendAccountCommand(owner.UserId, "unusable"), Ct);
                else
                    await mediator.Send(new ReinstateAccountCommand(owner.UserId, "unusable"), Ct);
            });
        }
        finally
        {
            accessor.HttpContext = previous;
        }
        (await AccessAsync(owner.UserId)).IsSuspended.ShouldBeFalse();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectCommand_ShouldRefuseOrdinaryCaller_WhenRealSessionAuthenticationHasSucceeded(bool suspend)
    {
        var owner = await OwnerAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = Ct };
        context.Request.Headers.Authorization = $"Bearer {owner.SessionId}";
        accessor.HttpContext = context;
        try
        {
            // The production handler supplies this principal and its session revision; no role claim is fabricated.
            var authentication = await context.AuthenticateAsync("Bearer");
            authentication.Succeeded.ShouldBeTrue();
            context.User = authentication.Principal.ShouldNotBeNull();
            scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.ShouldBe(owner.UserId);
            await Should.ThrowAsync<ForbiddenException>(async () =>
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                if (suspend)
                    await mediator.Send(new SuspendAccountCommand(owner.UserId, "unusable"), Ct);
                else
                    await mediator.Send(new ReinstateAccountCommand(owner.UserId, "unusable"), Ct);
            });
        }
        finally
        {
            accessor.HttpContext = previous;
        }
        (await AccessAsync(owner.UserId)).IsSuspended.ShouldBeFalse();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAccess_ShouldRequireOwnInboxProof_WhenAdministratorHasOnlyASession(bool suspend)
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var response = await admin.Client.PostAsJsonAsync(Path(owner.UserId, suspend), new { reauthGrant = "unusable" }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AccessAsync(owner.UserId)).IsSuspended.ShouldBeFalse();
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SuspendAndReinstate_ShouldAdvanceIndependentStateAndAuditActorTarget_WhenBothCommit()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var initial = await AccessAsync(owner.UserId);

        var suspended = await ChangeAsync(admin, owner.UserId, true);
        suspended.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstReceipt = await suspended.Content.ReadFromJsonAsync<JsonElement>(Ct);
        firstReceipt.GetProperty("userId").GetGuid().ShouldBe(owner.UserId);
        firstReceipt.GetProperty("isSuspended").GetBoolean().ShouldBeTrue();
        var blocked = await AccessAsync(owner.UserId);
        blocked.AccessRevision.ShouldBe(initial.AccessRevision + 1);
        blocked.CredentialCutoff.ShouldBeGreaterThan(initial.CredentialCutoff);
        blocked.DeletedAt.ShouldBeNull();
        blocked.Email.ShouldBe(owner.Email);

        var reinstated = await ChangeAsync(admin, owner.UserId, false);
        reinstated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var active = await AccessAsync(owner.UserId);
        active.IsSuspended.ShouldBeFalse();
        active.AccessRevision.ShouldBe(blocked.AccessRevision + 1);
        active.CredentialCutoff.ShouldBeGreaterThan(blocked.CredentialCutoff);
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);

        var rows = await AuditsAsync(owner.UserId);
        rows.Select(row => row.EventType).ShouldBe(["Admin.AccountSuspended", "Admin.AccountReinstated"]);
        rows.ShouldAllBe(row => row.Actor == admin.UserId && row.Target == owner.UserId);
        rows.ShouldAllBe(row => row.Payload == null || !row.Payload.Contains(owner.Email, StringComparison.Ordinal));
        foreach (var response in new[] { suspended, reinstated })
        {
            response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
            response.Headers.CacheControl.NoStore.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAccess_ShouldReturnConflictWithoutAuditOrRevisionChange_WhenAlreadyInRequestedState(bool suspend)
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        if (suspend)
            (await ChangeAsync(admin, owner.UserId, true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await AccessAsync(owner.UserId);
        var previousAudits = await AuditsAsync(owner.UserId);

        var refused = await ChangeAsync(admin, owner.UserId, suspend);

        await ShouldBeProblemAsync(refused, HttpStatusCode.Conflict,
            suspend ? AccountAccessErrors.AlreadySuspended : AccountAccessErrors.AlreadyReinstated);
        (await AccessAsync(owner.UserId)).ShouldBe(before);
        (await AuditsAsync(owner.UserId)).ShouldBe(previousAudits);
    }

    [Fact]
    public async Task Suspend_ShouldRefuseSelfWithoutChanges_WhenAdministratorProvesOwnInbox()
    {
        var admin = await AdminAsync();
        var before = await AccessAsync(admin.UserId);

        await ShouldBeProblemAsync(await ChangeAsync(admin, admin.UserId, true), HttpStatusCode.Conflict,
            AccountAccessErrors.SelfSuspension);

        (await AccessAsync(admin.UserId)).ShouldBe(before);
        (await MeAsync(admin.SessionId)).ShouldBe(HttpStatusCode.OK);
        (await AuditsAsync(admin.UserId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangeAccess_ShouldDistinguishMissingIdentityFromHistoricalOrphan_WhenTargetCannotBeActedOn(bool suspend)
    {
        var admin = await AdminAsync();
        await ShouldBeProblemAsync(await ChangeAsync(admin, Guid.NewGuid(), suspend), HttpStatusCode.NotFound,
            AccountAccessErrors.AccountNotFound);
        var orphan = await AdminAccountsKit.CreateWithoutProfileAsync(factory,
            AdminAccountsKit.Address(AdminAccountsKit.NewToken(), "historical-orphan"), Ct);
        await ShouldBeProblemAsync(await ChangeAsync(admin, orphan, suspend), HttpStatusCode.Gone,
            AccountAccessErrors.ProfileUnavailable);
        (await AccessAsync(orphan)).IsSuspended.ShouldBeFalse();
        (await AuditsAsync(orphan)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspend_ShouldRevokeExistingAndRotatedSessions_WhenStateCommits()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        Session rotated;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            var existing = (await sessions.GetAsync(SessionId.FromRaw(owner.SessionId), Ct)).ShouldNotBeNull();
            var interval = scope.ServiceProvider.GetRequiredService<IOptions<SessionStoreOptions>>().Value
                .ProfileFor(existing.Lifetime).RotationInterval;
            interval.ShouldBeGreaterThan(TimeSpan.Zero);
            // The clock reaches rotation eligibility; Redis payload, ACL and primary-account checks stay real.
            var later = new MutableFakeDateTimeProvider
            {
                UtcNow = existing.CreatedAt + interval + TimeSpan.FromSeconds(1),
            };
            var raw = ActivatorUtilities.CreateInstance<RedisSessionStore>(scope.ServiceProvider, later);
            var guarded = new AccessControlledSessionStore(raw,
                scope.ServiceProvider.GetRequiredService<IAccountAccessReader>(),
                scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>());
            var rotation = (await guarded.RotateAsync(existing.Id, Ct)).ShouldNotBeNull();
            rotated = (await sessions.GetAsync(rotation.NewId, Ct)).ShouldNotBeNull();
            rotated.AccessRevision.ShouldBe(existing.AccessRevision);
        }
        (await MeAsync(rotated.Id.Reveal())).ShouldBe(HttpStatusCode.OK);

        (await ChangeAsync(admin, owner.UserId, true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(rotated.Id.Reveal())).ShouldBe(HttpStatusCode.Unauthorized);
        await using var read = factory.Services.CreateAsyncScope();
        var store = read.ServiceProvider.GetRequiredService<ISessionStore>();
        (await store.RotateAsync(rotated.Id, Ct)).ShouldBeNull();
        (await ChangeAsync(admin, owner.UserId, false)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await store.GetAsync(rotated.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task ChangeAccess_ShouldPreservePendingDeletionAndReportIt_WhenSuspensionIsReinstated()
    {
        var admin = await AdminAsync();
        var token = AdminAccountsKit.NewToken();
        var owner = await AdminAccountsKit.CreatePendingDeletionAsync(factory, AdminAccountsKit.Address(token, "deleted"), Ct);
        var deletion = (await AccessAsync(owner)).DeletedAt.ShouldNotBeNull();

        (await ChangeAsync(admin, owner, true)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var suspended = await admin.Client.GetFromJsonAsync<JsonElement>(AdminAccountsKit.DetailPath(owner), Ct);
        suspended.GetProperty("status").GetString().ShouldBe("PendingDeletion");
        suspended.GetProperty("isSuspended").GetBoolean().ShouldBeTrue();
        var reinstated = await ChangeAsync(admin, owner, false);
        reinstated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reinstated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pendingDeletion").GetBoolean().ShouldBeTrue();
        var after = await AccessAsync(owner);
        after.DeletedAt.ShouldBe(deletion);
        after.CanAuthenticate.ShouldBeFalse();
    }

    [Fact]
    public async Task Search_ShouldFilterAndCountSuspendedAccounts_WhenDirectoryRefreshesAfterTransition()
    {
        var admin = await AdminAsync();
        var token = AdminAccountsKit.NewToken();
        var active = await AdminAccountsKit.OpenActiveAsync(factory, AdminAccountsKit.Address(token, "active"), Ct);
        var blocked = await AdminAccountsKit.OpenActiveAsync(factory, AdminAccountsKit.Address(token, "blocked"), Ct);
        (await ChangeAsync(admin, blocked, true)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await AdminAccountsKit.SearchOkAsync(admin.Client, new { address = token, status = "Suspended" }, Ct);

        AdminAccountsKit.Items(result).ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(blocked);
        var counts = result.GetProperty("counts");
        counts.GetProperty("active").GetInt32().ShouldBe(1);
        counts.GetProperty("suspended").GetInt32().ShouldBe(1);
        (await AccessAsync(active)).CanAuthenticate.ShouldBeTrue();
    }

    [Fact]
    public async Task Suspend_ShouldRollBackStateAndSuccessAudit_WhenAuditSaveFails()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        var before = await AccessAsync(owner.UserId);
        HttpResponseMessage refused;
        using (factory.AuditRowSaveFailure.FailingFor("Admin.AccountSuspended", admin.UserId))
            refused = await ChangeAsync(admin, owner.UserId, true);

        refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await AccessAsync(owner.UserId)).ShouldBe(before);
        (await AuditsAsync(owner.UserId)).ShouldBeEmpty();
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspend_ShouldReturnCommittedReceipt_WhenRedisCleanupFails()
    {
        var admin = await AdminAsync();
        var owner = await OwnerAsync();
        HttpResponseMessage response;
        using (factory.SessionTeardownFaults.FailingFor(owner.UserId))
            response = await ChangeAsync(admin, owner.UserId, true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AccessAsync(owner.UserId)).IsSuspended.ShouldBeTrue();
        (await AuditsAsync(owner.UserId)).ShouldHaveSingleItem();
        (await MeAsync(owner.SessionId)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConcurrentSuspensions_ShouldLeaveOneActorUsable_WhenAdministratorsTargetEachOther()
    {
        var first = await AdminAsync();
        var second = await AdminAsync();
        var firstGrant = await ReauthTestHelpers.MintGrantAsync(factory, first.Client, first.SessionId, first.Email, Ct);
        var secondGrant = await ReauthTestHelpers.MintGrantAsync(factory, second.Client, second.SessionId, second.Email, Ct);

        var replies = await Task.WhenAll(
            first.Client.PostAsJsonAsync(Path(second.UserId, true), new { reauthGrant = firstGrant }, Ct),
            second.Client.PostAsJsonAsync(Path(first.UserId, true), new { reauthGrant = secondGrant }, Ct));

        replies.Count(reply => reply.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        replies.Count(reply => reply.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(1);
        var states = new[] { await AccessAsync(first.UserId), await AccessAsync(second.UserId) };
        states.Count(state => state.IsEffectiveAdmin).ShouldBe(1);
        (await AuditsAsync(first.UserId)).Count.ShouldBe(states[0].IsSuspended ? 1 : 0);
        (await AuditsAsync(second.UserId)).Count.ShouldBe(states[1].IsSuspended ? 1 : 0);
    }
}
