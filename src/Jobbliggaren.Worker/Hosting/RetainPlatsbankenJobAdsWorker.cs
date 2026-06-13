using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.RetainPlatsbankenJobAds;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="RetainPlatsbankenJobAdsJob"/> som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2).
/// <para>
/// Timeout 300 sekunder (5 min) är generöst — körningen är en
/// <c>ExecuteUpdateAsync</c>-bulk-archive som typiskt tar sekunder. Paritet med
/// <see cref="SyncPlatsbankenSnapshotWorker"/>-disciplinen. ADR 0032-amendment
/// 2026-05-23.
/// </para>
/// </summary>
public sealed class RetainPlatsbankenJobAdsWorker(RetainPlatsbankenJobAdsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
