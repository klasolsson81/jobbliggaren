using System.Data.Common;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Feedback;

/// <summary>
/// #1979 — the dispatch job against real Postgres when a notice is deleted or moved while a run holds it: before the
/// submission read, before the claim save and before the outcome save. The notice's xmin makes a save on a row that
/// changed affect no row; the run then carries on without it. Each case is staged with an interceptor, so it runs every
/// time.
/// <para>
/// The clocks stand in early 2025, before any row another Worker test writes, so the oldest due notice a run here can
/// see is this test's own.
/// </para>
/// </summary>
[Collection("Worker")]
public sealed class FeedbackNotificationDispatchJobIntegrationTests(WorkerTestFixture fixture)
{
    private const string Recipient = "feedback-operator@example.test";
    private static readonly DateTimeOffset DeletedBeforeTheClaimAt = new(2025, 1, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeletedBetweenTheReadsAt = new(2025, 1, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeletedDuringTheSendAt = new(2025, 1, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OverlappingRunsAt = new(2025, 2, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReporterDeletedBeforeClaimAt = new(2025, 3, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReporterDeletedAfterClaimAt = new(2025, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly IEmailSender _sender = DeliveringSender();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Seeded(JobSeekerId Owner, FeedbackSubmissionId SubmissionId, FeedbackNotificationId NoticeId);

    private static IEmailSender DeliveringSender()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(true);
        return sender;
    }

    private FeedbackNotificationDispatchJob Job(AppDbContext db, DateTimeOffset now)
    {
        var gate = new FeedbackGate(
            Options.Create(new FeedbackOptions { Enabled = true, NotificationRecipient = Recipient }), _sender);
        return new FeedbackNotificationDispatchJob(
            db, _sender, gate, new FixedClock(now), NullLogger<FeedbackNotificationDispatchJob>.Instance);
    }

    /// <summary>The scope's own AppDbContext configuration, with one more interceptor.</summary>
    private static AppDbContext ContextWith(IServiceScope scope, IInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<AppDbContext>(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>())
            .AddInterceptors(interceptor)
            .Options);

    /// <summary>The submit handler's one save at <paramref name="at"/>: the submission, its notice, the page's first suppression.</summary>
    private async Task<Seeded> SubmitAsync(DateTimeOffset at)
    {
        var owner = await RegisterReporterAsync(at);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.JobSeekers.AsNoTracking().SingleAsync(value => value.Id == owner, Ct)).DeletedAt.ShouldBeNull();
        var submitted = FeedbackSubmission.Submit(
            owner, Guid.NewGuid(), FeedbackPage.Jobs, FeedbackRating.Create(4).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null, at);
        submitted.IsSuccess.ShouldBeTrue();
        var submission = submitted.Value;
        var notice = FeedbackNotification.QueueFor(submission);
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(notice);
        db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(owner, FeedbackPage.Jobs));
        await db.SaveChangesAsync(Ct);
        return new Seeded(owner, submission.Id, notice.Id);
    }

    private async Task<JobSeekerId> RegisterReporterAsync(DateTimeOffset at)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var coordinator = scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
        var address = $"feedback-reporter-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = address,
            Email = address,
            EmailConfirmed = true,
        };
        await using var transaction = await coordinator.BeginAsync([user.Id], lifecycle: false, Ct);
        (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user))
            .Succeeded.ShouldBeTrue();
        var clock = new FixedClock(at);
        var registered = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock);
        registered.IsSuccess.ShouldBeTrue();
        registered.Value.DeletedAt.ShouldBeNull();
        db.JobSeekers.Add(registered.Value);
        await db.SaveChangesAsync(Ct);
        (await scope.ServiceProvider.GetRequiredService<IAccountAccessReader>().ReadAsync(user.Id, Ct))
            .ShouldNotBeNull().CanAuthenticate.ShouldBeTrue();
        await transaction.CommitAsync(Ct);
        return registered.Value.Id;
    }

    private int SendsFor(Seeded seeded) =>
        _sender.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IEmailSender.SendFeedbackReceivedNotificationAsync)
            && call.GetArguments()[1] is FeedbackReceivedNotificationEmail content
            && content.FeedbackId == seeded.SubmissionId.Value);

    private async Task<FeedbackNotification?> NoticeAsync(Seeded seeded)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FeedbackNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == seeded.NoticeId, Ct);
    }

    [Fact]
    public async Task RunAsync_ANoticeDeletedBetweenItsReadAndItsClaim_IsNeverSentAndTheRunEndsCleanly()
    {
        var seeded = await SubmitAsync(DeletedBeforeTheClaimAt);
        // FeedbackRetentionJob is the actor: it deletes a notice, then its submission, once both are older than 90 days.
        // Its clock stands 91 days after this submission, so only rows this old are deleted.
        var retentionRuns = new BeforeTheClaimIsSaved(ct => RetentionAsync(DeletedBeforeTheClaimAt, ct));
        using var runScope = fixture.Services.CreateScope();
        await using var db = ContextWith(runScope, retentionRuns);

        await Should.NotThrowAsync(() => Job(db, DeletedBeforeTheClaimAt.AddMinutes(1)).RunAsync(Ct));

        retentionRuns.Fired.ShouldBe(1);
        SendsFor(seeded).ShouldBe(0);
        (await NoticeAsync(seeded)).ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_ANoticeDeletedBetweenItsReadAndItsSubmissionsRead_IsNotSentAndTheRunEndsCleanly()
    {
        var seeded = await SubmitAsync(DeletedBetweenTheReadsAt);
        // FeedbackRetentionJob is the actor, as above, this time between the job's read of the notice and of its submission.
        var retentionRuns = new BeforeTheSubmissionIsRead(ct => RetentionAsync(DeletedBetweenTheReadsAt, ct));
        using var runScope = fixture.Services.CreateScope();
        await using var db = ContextWith(runScope, retentionRuns);

        await Should.NotThrowAsync(() => Job(db, DeletedBetweenTheReadsAt.AddMinutes(1)).RunAsync(Ct));

        retentionRuns.Fired.ShouldBe(1);
        SendsFor(seeded).ShouldBe(0);
        (await NoticeAsync(seeded)).ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_ANoticeDeletedWhileItsMailIsSent_IsSentOnceAndTheRunEndsCleanly()
    {
        var seeded = await SubmitAsync(DeletedDuringTheSendAt);
        // FeedbackRetentionJob is the actor, as above, this time after the provider took the mail and before its outcome
        // is saved.
        var retentionRuns = new BeforeTheOutcomeIsSaved(ct => RetentionAsync(DeletedDuringTheSendAt, ct));
        using var runScope = fixture.Services.CreateScope();
        await using var db = ContextWith(runScope, retentionRuns);

        await Should.NotThrowAsync(() => Job(db, DeletedDuringTheSendAt.AddMinutes(1)).RunAsync(Ct));

        retentionRuns.Fired.ShouldBe(1);
        SendsFor(seeded).ShouldBe(1);
        (await NoticeAsync(seeded)).ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_AClaimThatLosesToAnOverlappingRun_SendsNothingAndTheNoticeGoesOutOnce()
    {
        // DECLARED UNREACHABLE (CLAUDE.md §5 Tests:) while Hangfire's DisableConcurrentExecution lock on
        // FeedbackNotificationDispatchWorker holds: two runs never overlap. Asserted only as the safe degradation if
        // they do — the losing claim's xmin no longer matches, so the notice is sent once, by the run that claimed it.
        var seeded = await SubmitAsync(OverlappingRunsAt);
        var now = OverlappingRunsAt.AddMinutes(1);
        var overlappingRun = new BeforeTheClaimIsSaved(async ct =>
        {
            using var scope = fixture.Services.CreateScope();
            await Job(scope.ServiceProvider.GetRequiredService<AppDbContext>(), now).RunAsync(ct);
        });
        using var runScope = fixture.Services.CreateScope();
        await using var db = ContextWith(runScope, overlappingRun);

        await Should.NotThrowAsync(() => Job(db, now).RunAsync(Ct));

        overlappingRun.Fired.ShouldBe(1);
        SendsFor(seeded).ShouldBe(1);
        var notice = (await NoticeAsync(seeded)).ShouldNotBeNull();
        notice.State.ShouldBe(FeedbackNotificationState.Accepted);
        notice.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ShouldRemoveTheNeverSentNoticeAndRetainFeedback_WhenReporterWasSoftDeletedBeforeClaim()
    {
        var target = await SubmitAsync(ReporterDeletedBeforeClaimAt);
        var control = await SubmitAsync(ReporterDeletedBeforeClaimAt.AddSeconds(1));
        var now = ReporterDeletedBeforeClaimAt.AddMinutes(1);
        await SoftDeleteReporterAsync(target.Owner, now, Ct);
        using var runScope = fixture.Services.CreateScope();

        await Job(runScope.ServiceProvider.GetRequiredService<AppDbContext>(), now).RunAsync(Ct);

        SendsFor(target).ShouldBe(0);
        (await NoticeAsync(target)).ShouldBeNull();
        await AssertDeletedReporterAndLiveControlAsync(target, control, now);
    }

    [Fact]
    public async Task RunAsync_ShouldRefuseTransportAndRetainFeedback_WhenReporterIsSoftDeletedAfterPersistedClaim()
    {
        var target = await SubmitAsync(ReporterDeletedAfterClaimAt);
        var control = await SubmitAsync(ReporterDeletedAfterClaimAt.AddSeconds(1));
        var now = ReporterDeletedAfterClaimAt.AddMinutes(1);
        var deletion = new SoftDeleteReporterAfterThePersistedClaim(target.NoticeId, async ct =>
        {
            using var probeScope = fixture.Services.CreateScope();
            var persisted = await probeScope.ServiceProvider.GetRequiredService<AppDbContext>()
                .FeedbackNotifications.AsNoTracking().SingleAsync(value => value.Id == target.NoticeId, ct);
            await SoftDeleteReporterAsync(target.Owner, now, ct);
            return persisted.State;
        });
        using var runScope = fixture.Services.CreateScope();
        await using var db = ContextWith(runScope, deletion);

        await Job(db, now).RunAsync(Ct);

        deletion.Fired.ShouldBe(1);
        deletion.SawPersistedSending.ShouldBeTrue();
        SendsFor(target).ShouldBe(0);
        (await NoticeAsync(target)).ShouldBeNull();
        await AssertDeletedReporterAndLiveControlAsync(target, control, now);
    }

    private async Task SoftDeleteReporterAsync(JobSeekerId owner, DateTimeOffset at, CancellationToken cancellationToken)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reporter = await db.JobSeekers.SingleAsync(value => value.Id == owner, cancellationToken);
        reporter.DeletedAt.ShouldBeNull();
        var coordinator = scope.ServiceProvider.GetRequiredService<IAccountAccessCoordinator>();
        await using var transaction = await coordinator.BeginAsync([reporter.UserId], lifecycle: true, cancellationToken);
        reporter.SoftDelete(new FixedClock(at));
        reporter.DeletedAt.ShouldBe(at);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AssertDeletedReporterAndLiveControlAsync(Seeded target, Seeded control, DateTimeOffset deletedAt)
    {
        SendsFor(control).ShouldBe(1);
        var controlNotice = (await NoticeAsync(control)).ShouldNotBeNull();
        controlNotice.State.ShouldBe(FeedbackNotificationState.Accepted);
        controlNotice.Attempts.ShouldBe(1);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.JobSeekers.AnyAsync(value => value.Id == target.Owner, Ct)).ShouldBeFalse();
        (await db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == target.Owner, Ct)).DeletedAt.ShouldBe(deletedAt);
        (await db.JobSeekers.AsNoTracking().SingleAsync(value => value.Id == control.Owner, Ct)).DeletedAt.ShouldBeNull();
        (await db.FeedbackSubmissions.AnyAsync(value => value.Id == target.SubmissionId, Ct)).ShouldBeTrue();
        (await db.FeedbackSubmissions.AnyAsync(value => value.Id == control.SubmissionId, Ct)).ShouldBeTrue();
    }

    /// <summary>A retention run whose clock stands 91 days after <paramref name="submittedAt"/>.</summary>
    private async Task RetentionAsync(DateTimeOffset submittedAt, CancellationToken cancellationToken)
    {
        using var scope = fixture.Services.CreateScope();
        await new FeedbackRetentionJob(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new FixedClock(submittedAt.AddDays(91)),
                NullLogger<FeedbackRetentionJob>.Instance)
            .RunAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="interleave"/> once, as the run's first read of a submission is about to execute.</summary>
    private sealed class BeforeTheSubmissionIsRead(Func<CancellationToken, Task> interleave) : DbCommandInterceptor
    {
        public int Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Fired == 0 && command.CommandText.Contains("FROM feedback_submissions", StringComparison.Ordinal))
            {
                Fired++;
                await interleave(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Runs <paramref name="interleave"/> once, as the save of an accepted outcome is about to execute.</summary>
    private sealed class BeforeTheOutcomeIsSaved(Func<CancellationToken, Task> interleave) : SaveChangesInterceptor
    {
        public int Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var records = eventData.Context is { } context && context.ChangeTracker.Entries<FeedbackNotification>()
                .Any(entry => entry.State == EntityState.Modified
                    && entry.Entity.State == FeedbackNotificationState.Accepted);
            if (records && Fired == 0)
            {
                Fired++;
                await interleave(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Runs <paramref name="interleave"/> once, as the first claim save of the run is about to execute.</summary>
    private sealed class BeforeTheClaimIsSaved(Func<CancellationToken, Task> interleave) : SaveChangesInterceptor
    {
        public int Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var claims = eventData.Context is { } context && context.ChangeTracker.Entries<FeedbackNotification>()
                .Any(entry => entry.State == EntityState.Modified
                    && entry.Entity.State == FeedbackNotificationState.Sending);
            if (claims && Fired == 0)
            {
                Fired++;
                await interleave(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Soft-deletes the reporter only after another PostgreSQL context observes the persisted claim.</summary>
    private sealed class SoftDeleteReporterAfterThePersistedClaim(
        FeedbackNotificationId noticeId,
        Func<CancellationToken, Task<FeedbackNotificationState>> interleave) : SaveChangesInterceptor
    {
        public int Fired { get; private set; }
        public bool SawPersistedSending { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var claimed = eventData.Context is { } context && context.ChangeTracker.Entries<FeedbackNotification>()
                .Any(entry => entry.Entity.Id == noticeId && entry.Entity.State == FeedbackNotificationState.Sending);
            if (claimed && Fired == 0)
            {
                Fired++;
                SawPersistedSending = await interleave(cancellationToken) == FeedbackNotificationState.Sending;
            }

            return result;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
