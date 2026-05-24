using Hangfire;
using JobbPilot.Application.JobAds.Jobs.BackfillJobAdSsyk;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// STEG 6 (2026-05-24) — Worker-wrapper för <see cref="BackfillJobAdSsykJob"/>
/// som applicerar Hangfire <see cref="DisableConcurrentExecutionAttribute"/>
/// utan att läcka Hangfire-beroende till Application-lagret (Clean Arch —
/// ADR 0023 delbeslut 2, samma mönster som
/// <see cref="SyncPlatsbankenSnapshotWorker"/> och
/// <see cref="BackfillFieldEncryptionWorker"/>).
///
/// <para>
/// Timeout 7200 s (2 h): vid default <c>PerItemDelayMs=200</c> tar 35k rader
/// ~117 min, plus per-item child-scope + Mediator-pipeline. Värre-fall vid
/// rate-limit-throttling eller transient retry kan ge ytterligare overhead.
/// Idempotent — avbruten körning plockas upp av nästa enqueue (NULL-rad-
/// filter är race-säker mot snapshot-cron).
/// </para>
/// </summary>
public sealed class BackfillJobAdSsykWorker(BackfillJobAdSsykJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 7200)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        job.RunAsync(cancellationToken);
}
