using Hangfire;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// ADR 0080 Vag 4 PR-4b — Hangfire wrapper for the Strong-match digest dispatch. Two entry points
/// (one per cadence) carry only the <see cref="DisableConcurrentExecutionAttribute"/>; the inner
/// <see cref="DigestDispatchJob"/> filters consenting users to the cadence it is invoked for (the
/// cron IS the window). Parity <see cref="BackgroundMatchingWorker"/>.
/// </summary>
public sealed class DigestDispatchWorker(DigestDispatchJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunDailyAsync(CancellationToken cancellationToken)
        => job.RunAsync(DigestCadence.Daily, cancellationToken);

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunWeeklyAsync(CancellationToken cancellationToken)
        => job.RunAsync(DigestCadence.Weekly, cancellationToken);
}
