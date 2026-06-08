using Hangfire;
using JobbPilot.Application.JobAds.Jobs.BackfillJobAdKlass2;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Fas B2 (2026-06-08, ADR 0067) — Worker-wrapper för
/// <see cref="BackfillJobAdKlass2Job"/> som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-
/// beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2, samma
/// mönster som <see cref="BackfillJobAdSsykWorker"/>).
///
/// <para>
/// Timeout 10800 s (3 h): vid default <c>PerItemDelayMs=200</c> tar ~44k rader
/// (hela tabellen innan re-ingest) ~2,5h, plus per-item child-scope +
/// Mediator-pipeline + transient retry-overhead. Idempotent — avbruten körning
/// plockas upp av nästa enqueue (NULL-rad-filter är race-säker mot snapshot-cron).
/// </para>
/// </summary>
public sealed class BackfillJobAdKlass2Worker(BackfillJobAdKlass2Job job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 10800)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        job.RunAsync(cancellationToken);
}
