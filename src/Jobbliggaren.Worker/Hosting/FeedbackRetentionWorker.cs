using Hangfire;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>#1979 — the daily 90-day feedback retention.</summary>
public sealed class FeedbackRetentionWorker(FeedbackRetentionJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
