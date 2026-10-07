using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.UnitTests.Feedback;

/// <summary>
/// Feedback as <c>SubmitFeedbackCommandHandler</c> saves it, for the tests of what reads or moves it afterwards: the
/// submission, its notice queued at the submission's instant, and the page's prompt suppression the first time the
/// owner sends feedback there. The handler's own factory calls with the handler's own arguments, so every seeded row
/// is one the handler writes at that instant (the clock is the only actor that varies).
/// </summary>
internal static class FeedbackRows
{
    /// <summary>What the handler stores when the browser reported nothing. A fresh instance, as the handler makes one per submission.</summary>
    public static ReportedClientContext NoClient() =>
        ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null);

    public static Task<Saved> SubmitAsync(
        AppDbContext db,
        JobSeekerId owner,
        FeedbackPage page,
        int? rating,
        string? comment,
        DateTimeOffset at,
        CancellationToken ct) =>
        SubmitAsync(db, owner, page, rating, comment, at, NoClient(), appVersion: null, ct);

    public static async Task<Saved> SubmitAsync(
        AppDbContext db,
        JobSeekerId owner,
        FeedbackPage page,
        int? rating,
        string? comment,
        DateTimeOffset at,
        ReportedClientContext client,
        string? appVersion,
        CancellationToken ct)
    {
        var submission = FeedbackSubmission.Submit(
            owner,
            Guid.NewGuid(),
            page,
            rating is { } stars ? FeedbackRating.Create(stars).Value : null,
            FeedbackComment.Create(comment).Value,
            client,
            appVersion,
            at).Value;
        var notice = FeedbackNotification.Queue(submission.Id, owner, submission.SubmittedAt);

        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(notice);
        if (!await db.FeedbackPromptSuppressions.AnyAsync(s => s.JobSeekerId == owner && s.Page == page, ct))
            db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(owner, page, at));

        await db.SaveChangesAsync(ct);
        db.ClearTracking();
        return new Saved(submission.Id, notice.Id);
    }

    internal sealed record Saved(FeedbackSubmissionId SubmissionId, FeedbackNotificationId NoticeId);
}
