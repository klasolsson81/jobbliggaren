using Hangfire;
using Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + C5) — Worker-wrapper för
/// <see cref="BackfillFieldEncryptionJob"/> som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023
/// delbeslut 2, samma mönster som <see cref="SyncPlatsbankenSnapshotWorker"/>).
///
/// <para>
/// Timeout 3600 s (1 h): backfillen itererar owner-batchar med per-owner
/// fresh scope + KMS-unwrap + load/save — potentiellt tiotals minuter vid
/// stor legacy-svans. <c>DisableConcurrentExecution</c> hindrar Hangfire
/// <c>AutomaticRetry</c> från att överlappa en pågående långkörning (samma
/// skydd som snapshot-jobbet). Idempotent → en avbruten körning plockas upp
/// av nästa cron utan biverkningar.
/// </para>
/// </summary>
public sealed class BackfillFieldEncryptionWorker(BackfillFieldEncryptionJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        job.RunAsync(cancellationToken);
}
