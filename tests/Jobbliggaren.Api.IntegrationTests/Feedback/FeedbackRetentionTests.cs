using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

[Collection("Api")]
public sealed class FeedbackRetentionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // The clock is the actor: the submit path's factories and normalized pixels, at the given instant.
    private static async Task<FeedbackSubmission> SaveAsync(
        IAppDbContext db, IFeedbackScreenshotNormalizer normalizer, JobSeekerId owner,
        DateTimeOffset submittedAt, DateTimeOffset triagedAt, CancellationToken ct)
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(20, 40, 60, 255));
        using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder(), ct);
        var normalized = await normalizer.NormalizeAsync(stream.ToArray(), ct);
        normalized.IsSuccess.ShouldBeTrue();
        var submission = FeedbackSubmission.Submit(owner, Guid.NewGuid(), FeedbackPage.Jobs,
            FeedbackRating.Create(4).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null),
            null, submittedAt).Value;
        submission.ChangeStatus(FeedbackStatus.Resolved, triagedAt).IsSuccess.ShouldBeTrue();
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(FeedbackNotification.QueueFor(submission));
        db.FeedbackScreenshots.Add(FeedbackScreenshot.AttachTo(submission,
            normalized.Value.Content, normalized.Value.Width, normalized.Value.Height).Value);
        if (!await db.FeedbackPromptSuppressions.AnyAsync(s => s.JobSeekerId == owner && s.Page == FeedbackPage.Jobs, ct))
            db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(owner, FeedbackPage.Jobs));
        await db.SaveChangesAsync(ct);
        db.ClearTracking();
        return submission;
    }

    [Fact]
    public async Task RunAsync_TheNinetyDayBoundary_DeletesOldImagesNoticesAndFeedbackButKeepsSuppression()
    {
        var reporter = await ReporterAsync(factory, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var normalizer = scope.ServiceProvider.GetRequiredService<IFeedbackScreenshotNormalizer>();
        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var cutoff = now.AddDays(-90);
        var old = await SaveAsync(db, normalizer, reporter.JobSeekerId, cutoff.AddTicks(-10), now, Ct);
        var boundary = await SaveAsync(db, normalizer, reporter.JobSeekerId, cutoff, now, Ct);
        var fresh = await SaveAsync(db, normalizer, reporter.JobSeekerId, cutoff.AddTicks(10), now, Ct);

        await new FeedbackRetentionJob(db, new FixedClock(now), NullLogger<FeedbackRetentionJob>.Instance).RunAsync(Ct);

        (await db.FeedbackSubmissions.AsNoTracking().Where(s => s.JobSeekerId == reporter.JobSeekerId)
            .Select(s => s.Id).ToListAsync(Ct)).ShouldBe([boundary.Id, fresh.Id], ignoreOrder: true);
        (await db.FeedbackScreenshots.AsNoTracking().Where(s => s.JobSeekerId == reporter.JobSeekerId)
            .Select(s => s.SubmissionId).ToListAsync(Ct)).ShouldBe([boundary.Id, fresh.Id], ignoreOrder: true);
        (await db.FeedbackNotifications.AsNoTracking().Where(n => n.JobSeekerId == reporter.JobSeekerId)
            .Select(n => n.SubmissionId).ToListAsync(Ct)).ShouldBe([boundary.Id, fresh.Id], ignoreOrder: true);
        (await db.FeedbackPromptSuppressions.CountAsync(s => s.JobSeekerId == reporter.JobSeekerId, Ct)).ShouldBe(1);
        (await db.FeedbackSubmissions.AnyAsync(s => s.Id == old.Id, Ct)).ShouldBeFalse();
        await new FeedbackRetentionJob(db, new FixedClock(now), NullLogger<FeedbackRetentionJob>.Instance).RunAsync(Ct);
        (await db.FeedbackScreenshots.CountAsync(s => s.JobSeekerId == reporter.JobSeekerId, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_AnAlreadyCancelledToken_LeavesTheExpiredImageAndItsFeedback()
    {
        var reporter = await ReporterAsync(factory, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
        var saved = await SaveAsync(db, scope.ServiceProvider.GetRequiredService<IFeedbackScreenshotNormalizer>(),
            reporter.JobSeekerId, now.AddDays(-91), now, Ct);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new FeedbackRetentionJob(
            db, new FixedClock(now), NullLogger<FeedbackRetentionJob>.Instance).RunAsync(cancelled.Token));

        (await db.FeedbackSubmissions.AnyAsync(s => s.Id == saved.Id, Ct)).ShouldBeTrue();
        (await db.FeedbackScreenshots.AnyAsync(s => s.SubmissionId == saved.Id, Ct)).ShouldBeTrue();
        (await db.FeedbackNotifications.AnyAsync(n => n.SubmissionId == saved.Id, Ct)).ShouldBeTrue();
    }
}
