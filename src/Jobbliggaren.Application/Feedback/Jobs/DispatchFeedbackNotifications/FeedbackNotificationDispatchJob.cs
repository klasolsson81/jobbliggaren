using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications;

/// <summary>
/// Sends the operator notices for saved feedback, every minute (#1979). The committed rows are the
/// queue; nothing enqueues work from the request.
/// <para>
/// Each notice is saved as Sending BEFORE the provider call, so a run that dies afterwards leaves
/// a row the next run turns Unknown instead of resending. A refusal the provider proves is retried
/// with backoff; an unknown outcome waits for an administrator. The run stops at the first send
/// that is not accepted, so an outage costs one notice per run rather than a whole batch
/// (senior-cto-advisor M7).
/// </para>
/// </summary>
public sealed partial class FeedbackNotificationDispatchJob(
    IAppDbContext db,
    IEmailSender emailSender,
    FeedbackGate gate,
    IDateTimeProvider clock,
    ILogger<FeedbackNotificationDispatchJob> logger)
{
    public const int MaxSendsPerRun = 10;

    /// <summary>The most notices handed to the provider in any 24 hours, across every account; the rest wait.</summary>
    public const int DailyBudget = 20;

    /// <summary>Under the one-minute schedule, so a run ends before the next one starts.</summary>
    public static readonly TimeSpan RunBudget = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await ExpireStaleSendsAsync(cancellationToken);

        if (gate.DeliverableRecipient is not { } recipient)
        {
            if (await db.FeedbackNotifications.AnyAsync(
                    n => n.State == FeedbackNotificationState.Queued, cancellationToken))
                LogNotDeliverable(gate.DispatchAvailability);
            return;
        }

        var started = clock.UtcNow;
        var since = started - BudgetWindow;
        var budgetLeft = DailyBudget - await db.FeedbackNotifications
            .CountAsync(n => n.SendingStartedAt >= since, cancellationToken);
        for (var sends = 0; sends < MaxSendsPerRun && clock.UtcNow - started < RunBudget; sends++)
        {
            var now = clock.UtcNow;
            var notification = await db.FeedbackNotifications
                .Where(n => n.State == FeedbackNotificationState.Queued && n.NextAttemptAt <= now)
                .OrderBy(n => n.NextAttemptAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (notification is null)
                return;

            if (budgetLeft <= 0)
            {
                LogDailyBudgetSpent(DailyBudget);
                return;
            }

            var submission = await db.FeedbackSubmissions
                .AsNoTracking()
                .Where(s => s.Id == notification.SubmissionId)
                .Select(s => new { s.JobSeekerId, s.Page, s.Rating, s.SubmittedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (submission is null || !await HasLiveReporterAsync(submission.JobSeekerId, cancellationToken))
            {
                // Retention or an account deletion took the submission between the two reads, and
                // usually the notice with it.
                db.FeedbackNotifications.Remove(notification);
                await TrySaveAsync(CancellationToken.None);
                db.ClearTracking();
                continue;
            }

            if (notification.Claim(now).IsFailure)
                return;

            if (!await TrySaveAsync(cancellationToken))
            {
                db.ClearTracking();
                return;
            }

            if (!await HasLiveReporterAsync(submission.JobSeekerId, cancellationToken))
            {
                db.FeedbackNotifications.Remove(notification);
                await TrySaveAsync(CancellationToken.None);
                db.ClearTracking();
                continue;
            }

            budgetLeft--;
            var accepted = await SendAsync(recipient, notification, submission.Page, submission.Rating,
                submission.SubmittedAt, cancellationToken);
            await TrySaveAsync(CancellationToken.None);
            db.ClearTracking();

            if (!accepted)
                return;
        }
    }

    private Task<bool> HasLiveReporterAsync(JobSeekerId profileId, CancellationToken cancellationToken) =>
        db.JobSeekers.AsNoTracking().AnyAsync(seeker => seeker.Id == profileId, cancellationToken);

    private async Task<bool> SendAsync(
        string recipient,
        FeedbackNotification notification,
        FeedbackPage page,
        FeedbackRating? rating,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        var content = new FeedbackReceivedNotificationEmail(
            page, rating?.Value, submittedAt, notification.SubmissionId.Value);

        try
        {
            await emailSender.SendFeedbackReceivedNotificationAsync(recipient, content, cancellationToken);
            notification.RecordAccepted(clock.UtcNow);
            return true;
        }
        catch (EmailDeliveryException ex) when (ex.Disposition == EmailDeliveryDisposition.NotAccepted)
        {
            notification.RecordNotAccepted(clock.UtcNow);
            LogNotAccepted(notification.Attempts, notification.State);
            return false;
        }
        catch (EmailDeliveryException)
        {
            notification.RecordUnknown(clock.UtcNow);
            LogUnknownOutcome();
            return false;
        }
    }

    private async Task ExpireStaleSendsAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var threshold = now - FeedbackNotification.StaleSendingAfter;
        var stale = await db.FeedbackNotifications
            .Where(n => n.State == FeedbackNotificationState.Sending && n.SendingStartedAt <= threshold)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
            return;

        foreach (var notification in stale)
            notification.ExpireIfStale(now);

        var saved = await TrySaveAsync(CancellationToken.None);
        db.ClearTracking();
        if (saved)
            LogExpired(stale.Count);
    }

    /// <summary>
    /// Saves the tracked notices, or returns false when a row was deleted or changed after it was
    /// read. Retention and an account deletion delete notices while a run holds them; the next run
    /// reads what is left.
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            LogNoticeChangedDuringRun();
            return false;
        }
    }

    [LoggerMessage(3101, LogLevel.Warning,
        "[FeedbackNotificationDispatch] Notices are not sent: feedback availability is {Availability}")]
    private partial void LogNotDeliverable(FeedbackAvailability availability);

    [LoggerMessage(3102, LogLevel.Warning,
        "[FeedbackNotificationDispatch] The provider refused a notice (attempt {Attempts}); it is now {State}")]
    private partial void LogNotAccepted(int attempts, FeedbackNotificationState state);

    [LoggerMessage(3103, LogLevel.Error,
        "[FeedbackNotificationDispatch] A notice's outcome is unknown; it waits for an administrator")]
    private partial void LogUnknownOutcome();

    [LoggerMessage(3104, LogLevel.Error,
        "[FeedbackNotificationDispatch] {Count} notices were left sending past their window and are now unknown")]
    private partial void LogExpired(int count);

    [LoggerMessage(3107, LogLevel.Warning,
        "[FeedbackNotificationDispatch] The daily budget of {Budget} notices is spent; the rest wait in the queue")]
    private partial void LogDailyBudgetSpent(int budget);

    [LoggerMessage(3106, LogLevel.Information,
        "[FeedbackNotificationDispatch] A notice was deleted or changed while the run held it; the next run reads what is left")]
    private partial void LogNoticeChangedDuringRun();
}
