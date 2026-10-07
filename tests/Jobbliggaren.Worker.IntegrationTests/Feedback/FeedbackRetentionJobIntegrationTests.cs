using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Feedback;

/// <summary>
/// #1979 — the 90-day feedback retention against real Postgres: it deletes with <c>ExecuteDeleteAsync</c>, which EF
/// InMemory cannot run. A submission and its notice go together once older than 90 days (strictly); the per-page
/// prompt suppressions stay, because they live as long as the account. Rows are written as the submit handler writes
/// them, at an instant the clock chose; the retention reads only the two timestamps, so the owner needs no account row
/// (no foreign key, ADR 0011 — the precedent is ParsedResumeRetentionJobIntegrationTests).
/// </summary>
[Collection("Worker")]
public sealed class FeedbackRetentionJobIntegrationTests(WorkerTestFixture fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Seeded(FeedbackSubmissionId SubmissionId, FeedbackNotificationId NoticeId);

    /// <summary>The submit handler's one save at <paramref name="at"/>: the submission, its notice, the page's first suppression.</summary>
    private async Task<Seeded> SubmitAsync(JobSeekerId owner, FeedbackPage page, DateTimeOffset at)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var submission = FeedbackSubmission.Submit(
            owner, Guid.NewGuid(), page, FeedbackRating.Create(3).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null, at).Value;
        var notice = FeedbackNotification.Queue(submission.Id, owner, submission.SubmittedAt);
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(notice);
        if (!await db.FeedbackPromptSuppressions.AnyAsync(s => s.JobSeekerId == owner && s.Page == page, Ct))
            db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(owner, page, at));
        await db.SaveChangesAsync(Ct);
        return new Seeded(submission.Id, notice.Id);
    }

    /// <summary>FeedbackNotificationDispatchJob's own transforms for an accepted send: the claim, then the outcome.</summary>
    private async Task AcceptAsync(Seeded seeded, DateTimeOffset at)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notice = await db.FeedbackNotifications.SingleAsync(n => n.Id == seeded.NoticeId, Ct);
        notice.Claim(at).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
        notice.RecordAccepted(at).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new FeedbackRetentionJob(db, new FixedClock(now), NullLogger<FeedbackRetentionJob>.Instance).RunAsync(Ct);
    }

    [Fact]
    public async Task RunAsync_ResolvesFromTheWorkerGraph_AndRunsOnTheMigratedSchema()
    {
        using var scope = fixture.Services.CreateScope();
        var worker = scope.ServiceProvider.GetRequiredService<FeedbackRetentionWorker>();

        await Should.NotThrowAsync(() => worker.RunAsync(Ct));
    }

    [Fact]
    public async Task RunAsync_DeletesFeedbackAndNoticesOlderThan90Days_KeepsNewerOnesAndEverySuppression()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        var old = await SubmitAsync(owner, FeedbackPage.Jobs, FixedNow.AddDays(-91));
        await SubmitAsync(owner, FeedbackPage.Cv, FixedNow.AddDays(-90).AddSeconds(-1));
        var atTheCutoff = await SubmitAsync(owner, FeedbackPage.Overview, FixedNow.AddDays(-90));
        var fresh = await SubmitAsync(owner, FeedbackPage.Matches, FixedNow.AddDays(-10));
        await AcceptAsync(old, FixedNow.AddDays(-91).AddMinutes(1));

        await RunAsync(FixedNow);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FeedbackSubmissions.AsNoTracking().Where(s => s.JobSeekerId == owner).Select(s => s.Id).ToListAsync(Ct))
            .ShouldBe([atTheCutoff.SubmissionId, fresh.SubmissionId], ignoreOrder: true,
                "older than 90 days is deleted, a submission exactly 90 days old is kept (strict <)");
        (await db.FeedbackNotifications.AsNoTracking().Where(n => n.JobSeekerId == owner).Select(n => n.Id).ToListAsync(Ct))
            .ShouldBe([atTheCutoff.NoticeId, fresh.NoticeId], ignoreOrder: true,
                "a notice goes with its submission, whatever state the dispatch left it in");
        (await db.FeedbackPromptSuppressions.AsNoTracking().Where(s => s.JobSeekerId == owner).Select(s => s.Page).ToListAsync(Ct))
            .ShouldBe([FeedbackPage.Jobs, FeedbackPage.Cv, FeedbackPage.Overview, FeedbackPage.Matches], ignoreOrder: true,
                "the prompt suppressions outlive the feedback, so the prompt never returns on its own");
    }

    [Fact]
    public async Task RunAsync_ASecondRun_DeletesNothingMore()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        await SubmitAsync(owner, FeedbackPage.Jobs, FixedNow.AddDays(-120));
        var kept = await SubmitAsync(owner, FeedbackPage.Jobs, FixedNow.AddDays(-30));
        await RunAsync(FixedNow);

        await RunAsync(FixedNow);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FeedbackSubmissions.AsNoTracking().Where(s => s.JobSeekerId == owner).Select(s => s.Id).ToListAsync(Ct))
            .ShouldBe([kept.SubmissionId]);
        (await db.FeedbackPromptSuppressions.AsNoTracking().CountAsync(s => s.JobSeekerId == owner, Ct)).ShouldBe(1);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
