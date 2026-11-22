using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #2264 (ADR 0187) — the Hangfire wrapper for <see cref="ClassifyJuniorJobAdsJob"/>, keeping Hangfire out of
/// Application (ADR 0023 delbeslut 2). The lock wait is below the two-minute cadence and the job's own time budget,
/// so a tick blocked by a running one fails fast instead of queueing; with no automatic retry that failure is terminal
/// and benign — the next tick re-derives the same stale set from the committed state.
/// </summary>
public sealed class ClassifyJuniorJobAdsWorker(ClassifyJuniorJobAdsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 20)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
