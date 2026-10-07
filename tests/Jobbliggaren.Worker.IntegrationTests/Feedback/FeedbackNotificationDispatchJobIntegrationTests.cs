using System.Data.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
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

    private readonly IEmailSender _sender = DeliveringSender();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Seeded(FeedbackSubmissionId SubmissionId, FeedbackNotificationId NoticeId);

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
    private async Task<Seeded> SubmitAsync(JobSeekerId owner, DateTimeOffset at)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = FeedbackSubmission.Submit(
            owner, Guid.NewGuid(), FeedbackPage.Jobs, FeedbackRating.Create(4).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null, at).Value;
        var notice = FeedbackNotification.Queue(submission.Id, owner, submission.SubmittedAt);
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(notice);
        db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(owner, FeedbackPage.Jobs, at));
        await db.SaveChangesAsync(Ct);
        return new Seeded(submission.Id, notice.Id);
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
        var seeded = await SubmitAsync(new JobSeekerId(Guid.NewGuid()), DeletedBeforeTheClaimAt);
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
        var seeded = await SubmitAsync(new JobSeekerId(Guid.NewGuid()), DeletedBetweenTheReadsAt);
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
        var seeded = await SubmitAsync(new JobSeekerId(Guid.NewGuid()), DeletedDuringTheSendAt);
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
        var seeded = await SubmitAsync(new JobSeekerId(Guid.NewGuid()), OverlappingRunsAt);
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
