using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;

/// <summary>
/// Saves one feedback submission, its operator notice and — the first time for this page — the
/// prompt suppression, in one save. A key seen before replays its submission. The save happens
/// here rather than in the unit of work because a unique violation must turn into a replay
/// (precedent: <c>SaveJobAdCommandHandler</c>).
/// </summary>
public sealed class SubmitFeedbackCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    FeedbackGate gate,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector)
    : ICommandHandler<SubmitFeedbackCommand, Result<FeedbackSubmitted>>
{
    public async ValueTask<Result<FeedbackSubmitted>> Handle(
        SubmitFeedbackCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<FeedbackSubmitted>(DomainError.Validation(
                AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att skicka feedback."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (jobSeekerId == default)
            return Result.Failure<FeedbackSubmitted>(DomainError.NotFound("JobSeeker", userId));

        // A lost response is answered even if the gate has closed since.
        if (await FindByKeyAsync(jobSeekerId, command.SubmissionKey, cancellationToken) is { } earlier)
            return Result.Success(new FeedbackSubmitted(earlier, Replayed: true));

        if (!gate.AcceptsSubmissions)
            return Result.Failure<FeedbackSubmitted>(DomainError.NotFound(
                "Feedback.Closed", "Det går inte att skicka feedback just nu."));

        var submission = Build(jobSeekerId, command);
        if (submission.IsFailure)
            return Result.Failure<FeedbackSubmitted>(submission.Error);

        var notification = FeedbackNotification.Queue(submission.Value.Id, jobSeekerId, submission.Value.SubmittedAt);

        // Two attempts. A unique violation is either the same key saved concurrently (replay it) or
        // a concurrent submission for the same page claiming the suppression row (save again
        // without it). The inspector cannot tell the two indexes apart, so the re-read decides.
        for (var attempt = 1; ; attempt++)
        {
            var suppression = await SuppressionFor(jobSeekerId, submission.Value.Page, cancellationToken);
            db.FeedbackSubmissions.Add(submission.Value);
            db.FeedbackNotifications.Add(notification);
            if (suppression is not null)
                db.FeedbackPromptSuppressions.Add(suppression);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success(new FeedbackSubmitted(submission.Value.Id.Value, Replayed: false));
            }
            catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
            {
                db.ClearTracking();

                if (await FindByKeyAsync(jobSeekerId, command.SubmissionKey, cancellationToken) is { } winner)
                    return Result.Success(new FeedbackSubmitted(winner, Replayed: true));

                if (attempt == 2)
                    throw new ConcurrencyConflictException();
            }
        }
    }

    private Result<FeedbackSubmission> Build(JobSeekerId jobSeekerId, SubmitFeedbackCommand command)
    {
        if (!FeedbackPage.TryFromKey(command.PageKey, out var page))
            return Result.Failure<FeedbackSubmission>(DomainError.Validation(
                "Feedback.UnknownPage", "Sidan går inte att ge feedback på."));

        FeedbackRating? rating = null;
        if (command.Rating is { } stars)
        {
            var created = FeedbackRating.Create(stars);
            if (created.IsFailure)
                return Result.Failure<FeedbackSubmission>(created.Error);
            rating = created.Value;
        }

        var comment = FeedbackComment.Create(command.Comment);
        if (comment.IsFailure)
            return Result.Failure<FeedbackSubmission>(comment.Error);

        var client = command.Client;
        var context = ReportedClientContext.FromReported(
            client.ViewportWidth, client.ViewportHeight, client.ScreenWidth, client.ScreenHeight,
            client.PixelRatio, client.Theme, client.DeviceClass, client.OsFamily, client.BrowserFamily);

        return FeedbackSubmission.Submit(
            jobSeekerId, command.SubmissionKey, page!, rating, comment.Value, context, command.AppVersion,
            clock.UtcNow);
    }

    private async Task<FeedbackPromptSuppression?> SuppressionFor(
        JobSeekerId jobSeekerId, FeedbackPage page, CancellationToken cancellationToken)
    {
        var exists = await db.FeedbackPromptSuppressions
            .AsNoTracking()
            .AnyAsync(s => s.JobSeekerId == jobSeekerId && s.Page == page, cancellationToken);
        return exists ? null : FeedbackPromptSuppression.Record(jobSeekerId, page, clock.UtcNow);
    }

    private async Task<Guid?> FindByKeyAsync(
        JobSeekerId jobSeekerId, Guid submissionKey, CancellationToken cancellationToken)
    {
        var id = await db.FeedbackSubmissions
            .AsNoTracking()
            .Where(s => s.JobSeekerId == jobSeekerId && s.SubmissionKey == submissionKey)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id == default ? null : id.Value;
    }
}
