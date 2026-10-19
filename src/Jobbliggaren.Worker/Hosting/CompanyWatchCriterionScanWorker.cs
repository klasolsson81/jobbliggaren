using Hangfire;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// The Hangfire wrapper for <see cref="CompanyWatchCriterionScanJob"/> (ADR 0175 D11), so the attributes stay out of
/// the Application layer (ADR 0023).
/// <para>
/// <b>No retry, no overlap.</b> The scan is level-triggered by each criterion's <c>ScannedThrough</c>, so the next
/// night repairs a failed run, and with <c>Attempts = 0</c> a failure stays a visible Failed state instead of a
/// retry racing the next night. The 1 800-second timeout is the lock's ACQUISITION wait, keyed per
/// <c>Type.Method</c>: it shares no lock with the follow scan or the materialisation (ADR 0175 D2).
/// </para>
/// </summary>
public sealed class CompanyWatchCriterionScanWorker(CompanyWatchCriterionScanJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
