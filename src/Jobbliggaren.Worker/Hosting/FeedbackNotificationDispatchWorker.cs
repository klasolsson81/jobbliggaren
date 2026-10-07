using Hangfire;
using Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #1979 — the minutely notice dispatch. No Hangfire retry: a retried run could resend a mail whose
/// outcome the job already recorded as unknown, and the job's own state machine owns every retry.
/// The lock wait stays under the one-minute schedule (senior-cto-advisor M7).
/// </summary>
public sealed class FeedbackNotificationDispatchWorker(FeedbackNotificationDispatchJob job)
{
    [DisableConcurrentExecution("feedback-notification-dispatch", 30)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
