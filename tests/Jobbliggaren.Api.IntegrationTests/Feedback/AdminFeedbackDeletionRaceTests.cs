using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// Native review #2061: a requeue and deletion share the reporter's real account lock.
/// The registrar, feedback endpoint and dispatcher's admitted claim/outcome transforms produce every premise.
/// </summary>
[Collection("Api")]
public sealed class AdminFeedbackDeletionRaceTests(ApiFactory factory)
{
    private const string DeletePath = "/api/v1/me/delete";
    private const string RequeueEvent = "Admin.FeedbackNotificationRequeued";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private sealed record NoticeSnapshot(
        FeedbackNotificationState State,
        int Attempts,
        DateTimeOffset NextAttemptAt,
        DateTimeOffset? StateChangedAt,
        DateTimeOffset? SendingStartedAt,
        DateTimeOffset? AcceptedAt);

    private sealed record AuditSnapshot(Guid? Actor, string AggregateType, string? Payload);

    private async Task<(FeedbackKit.Reporter Reporter, Guid SubmissionId, NoticeSnapshot Notice)> UnknownAsync()
    {
        var reporter = await FeedbackKit.ReporterAsync(factory, Ct);
        var id = await FeedbackKit.SubmitNewAsync(reporter.Client, "jobs", 3, null, Ct);
        await FeedbackKit.LoseTheOutcomeAsync(factory, id, Ct);
        var notice = await NoticeAsync(id);
        notice.State.ShouldBe(FeedbackNotificationState.Unknown);
        notice.Attempts.ShouldBe(1);
        (await AccessAsync(reporter.UserId)).HasLiveProfile.ShouldBeTrue();
        return (reporter, id, notice);
    }

    private WebApplicationFactory<Program> GatedHost(AccountLifecycleRaceGate gate) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(gate);
            services.RemoveAll<IAccountAccessCoordinator>();
            services.AddScoped<IAccountAccessCoordinator, LifecycleRaceCoordinator>();
        }));

    private async Task<AccountAccessSnapshot> AccessAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(userId, Ct))
            .ShouldNotBeNull();
    }

    private async Task<NoticeSnapshot> NoticeAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FeedbackNotifications.AsNoTracking()
            .Where(notice => notice.SubmissionId == new FeedbackSubmissionId(id))
            .Select(notice => new NoticeSnapshot(notice.State, notice.Attempts, notice.NextAttemptAt,
                notice.StateChangedAt, notice.SendingStartedAt, notice.AcceptedAt)).SingleAsync(Ct);
    }

    private async Task<IReadOnlyList<AuditSnapshot>> AuditsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(audit => audit.AggregateId == id && audit.EventType == RequeueEvent)
            .Select(audit => new AuditSnapshot(audit.UserId, audit.AggregateType, audit.Payload)).ToListAsync(Ct);
    }

    private async Task WaitForAdvisoryLockWaiterAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """).SingleAsync(timeout.Token) == 0)
            await Task.Delay(20, timeout.Token);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync(Ct));
        (await FeedbackKit.ProblemTitleAsync(response, Ct)).ShouldBe(code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequeueAndDelete_ShouldSerializeOnTheReporter_AndAuditOnlyTheCommittedRequeue(bool deletionFirst)
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var (reporter, id, before) = await UnknownAsync();
        var ownerSession = reporter.Client.DefaultRequestHeaders.Authorization.ShouldNotBeNull().Parameter.ShouldNotBeNull();
        var requeuePath = FeedbackKit.RequeuePath(id);
        using var gate = new AccountLifecycleRaceGate(reporter.UserId,
            deletionFirst ? DeletePath : requeuePath, deletionFirst ? requeuePath : DeletePath,
            firstLifecycle: deletionFirst, secondLifecycle: !deletionFirst, holdSecond: true);
        using var host = GatedHost(gate);
        using var client = host.CreateClient();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, client, ownerSession, reporter.Email, Ct);

        Task<HttpResponseMessage> DeleteAsync() => ReauthTestHelpers.PostAsSessionAsync(
            client, ownerSession, DeletePath, new { reauthGrant = grant }, Ct);
        Task<HttpResponseMessage> RequeueAsync() => ReauthTestHelpers.PostAsSessionAsync(
            client, admin.SessionId, requeuePath, new { acknowledgeDuplicateRisk = true }, Ct);

        try
        {
            var first = deletionFirst ? DeleteAsync() : RequeueAsync();
            (await gate.FirstHeld.Task.WaitAsync(Deadline, Ct))
                .ShouldBe(new AccountLifecycleRaceGate.HeldTransaction(deletionFirst, true, true, true));
            var second = deletionFirst ? RequeueAsync() : DeleteAsync();
            await gate.SecondAttempted.Task.WaitAsync(Deadline, Ct);
            await WaitForAdvisoryLockWaiterAsync();
            second.IsCompleted.ShouldBeFalse();
            // Requeue's Begin is after immutable submission/profile resolution; it now waits for the reporter.
            gate.Release();
            using var firstResponse = await first.WaitAsync(Deadline, Ct);
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await firstResponse.Content.ReadAsStringAsync(Ct));
            (await gate.SecondHeld.Task.WaitAsync(Deadline, Ct))
                .ShouldBe(new AccountLifecycleRaceGate.HeldTransaction(!deletionFirst, true, true, true));

            if (deletionFirst)
            {
                (await AccessAsync(reporter.UserId)).DeletedAt.ShouldNotBeNull();
                (await NoticeAsync(id)).ShouldBe(before);
                (await AuditsAsync(id)).ShouldBeEmpty();
            }
            else
            {
                (await AccessAsync(reporter.UserId)).HasLiveProfile.ShouldBeTrue();
                var committed = await NoticeAsync(id);
                committed.State.ShouldBe(FeedbackNotificationState.Queued);
                committed.Attempts.ShouldBe(0);
                (await AuditsAsync(id)).ShouldHaveSingleItem().ShouldBe(new AuditSnapshot(admin.UserId, "Feedback", null));
            }

            gate.ReleaseSecond();
            using var secondResponse = await second.WaitAsync(Deadline, Ct);
            if (deletionFirst)
                await ProblemAsync(secondResponse, HttpStatusCode.Gone, "Feedback.ReporterUnavailable");
            else
                secondResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent, await secondResponse.Content.ReadAsStringAsync(Ct));

            (await AccessAsync(reporter.UserId)).DeletedAt.ShouldNotBeNull();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.JobSeekers.AnyAsync(profile => profile.Id == reporter.JobSeekerId, Ct)).ShouldBeFalse();
            (await db.FeedbackSubmissions.AnyAsync(submission => submission.Id == new FeedbackSubmissionId(id), Ct))
                .ShouldBeTrue();
            (await AuditsAsync(id)).Count.ShouldBe(deletionFirst ? 0 : 1);
            if (deletionFirst)
                (await NoticeAsync(id)).ShouldBe(before);
            else
                (await NoticeAsync(id)).State.ShouldBe(FeedbackNotificationState.Queued);
        }
        finally
        {
            gate.Release();
            gate.ReleaseSecond();
        }
    }

    [Fact]
    public async Task Requeue_ShouldRefuseThePreviouslyAuthenticatedActor_WhenRealSuspensionWinsTheLock()
    {
        var supervisor = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var actor = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var (reporter, id, before) = await UnknownAsync();
        var suspendPath = $"/api/v1/admin/accounts/{actor.UserId}/suspend";
        var requeuePath = FeedbackKit.RequeuePath(id);
        using var gate = new AccountLifecycleRaceGate(actor.UserId, suspendPath, requeuePath, secondLifecycle: false);
        using var host = GatedHost(gate);
        using var client = host.CreateClient();
        var grant = await ReauthTestHelpers.MintGrantAsync(factory, client, supervisor.SessionId, supervisor.Email, Ct);
        var actorBefore = await AccessAsync(actor.UserId);
        actorBefore.IsEffectiveAdmin.ShouldBeTrue();

        try
        {
            var suspension = ReauthTestHelpers.PostAsSessionAsync(client, supervisor.SessionId,
                suspendPath, new { reauthGrant = grant }, Ct);
            (await gate.FirstHeld.Task.WaitAsync(Deadline, Ct))
                .ShouldBe(new AccountLifecycleRaceGate.HeldTransaction(true, true, true, true));
            var requeue = ReauthTestHelpers.PostAsSessionAsync(client, actor.SessionId,
                requeuePath, new { acknowledgeDuplicateRisk = true }, Ct);
            await gate.SecondAttempted.Task.WaitAsync(Deadline, Ct);
            await WaitForAdvisoryLockWaiterAsync();
            requeue.IsCompleted.ShouldBeFalse();
            gate.Release();
            using var suspended = await suspension.WaitAsync(Deadline, Ct);
            suspended.StatusCode.ShouldBe(HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync(Ct));
            using var refused = await requeue.WaitAsync(Deadline, Ct);
            await ProblemAsync(refused, HttpStatusCode.Unauthorized, "Auth.InvalidCredentials");

            var actorAfter = await AccessAsync(actor.UserId);
            actorAfter.IsSuspended.ShouldBeTrue();
            actorAfter.AccessRevision.ShouldBe(actorBefore.AccessRevision + 1);
            (await AccessAsync(reporter.UserId)).HasLiveProfile.ShouldBeTrue();
            (await NoticeAsync(id)).ShouldBe(before);
            (await AuditsAsync(id)).ShouldBeEmpty();
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task Requeue_ShouldRollBackTheNoticeAndAudit_WhenTheAuditSaveActuallyFails()
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var (reporter, id, before) = await UnknownAsync();
        using (factory.AuditRowSaveFailure.FailingFor(RequeueEvent, admin.UserId))
        {
            using var response = await admin.Client.PostAsJsonAsync(FeedbackKit.RequeuePath(id),
                new { acknowledgeDuplicateRisk = true }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        factory.AuditRowSaveFailure.LastFailedUserId.ShouldBe(admin.UserId);
        (await NoticeAsync(id)).ShouldBe(before);
        (await AuditsAsync(id)).ShouldBeEmpty();
        (await AccessAsync(reporter.UserId)).HasLiveProfile.ShouldBeTrue();
        using var retried = await admin.Client.PostAsJsonAsync(FeedbackKit.RequeuePath(id),
            new { acknowledgeDuplicateRisk = true }, Ct);
        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent, await retried.Content.ReadAsStringAsync(Ct));
        (await AuditsAsync(id)).ShouldHaveSingleItem().Actor.ShouldBe(admin.UserId);
    }

    [Fact]
    public async Task Requeue_ShouldReportUnknownWithoutReplay_WhenTheRealCommitAcknowledgementIsLost()
    {
        var admin = await AccountEmailChangeKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var (reporter, id, _) = await UnknownAsync();
        using (factory.CommitAcknowledgementLoss.AfterFeedbackRequeueCommit(id))
        {
            using var response = await admin.Client.PostAsJsonAsync(FeedbackKit.RequeuePath(id),
                new { acknowledgeDuplicateRisk = true }, Ct);
            await ProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Admin.AccountAccessOutcomeUnknown");
        }

        factory.CommitAcknowledgementLoss.Fired.ShouldBe(1);
        var committed = await NoticeAsync(id);
        committed.State.ShouldBe(FeedbackNotificationState.Queued);
        committed.Attempts.ShouldBe(0);
        (await AuditsAsync(id)).ShouldHaveSingleItem().ShouldBe(new AuditSnapshot(admin.UserId, "Feedback", null));
        (await AccessAsync(reporter.UserId)).HasLiveProfile.ShouldBeTrue();
        using var retry = await admin.Client.PostAsJsonAsync(FeedbackKit.RequeuePath(id),
            new { acknowledgeDuplicateRisk = true }, Ct);
        await ProblemAsync(retry, HttpStatusCode.Conflict, "Feedback.NotificationNotRequeueable");
        (await NoticeAsync(id)).ShouldBe(committed);
        (await AuditsAsync(id)).ShouldHaveSingleItem();
    }
}
