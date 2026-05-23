using Hangfire;
using JobbPilot.Application.JobAds.Jobs.ExpireJobAds;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="ExpireJobAdsJob"/> som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-
/// beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2).
/// <para>
/// Timeout 300 sekunder (5 min). ExecuteUpdateAsync-bulk-archive tar sekunder.
/// Defense-in-depth-pass (ADR 0032-amendment 2026-05-23) för annonser där
/// ExpiresAt passerat men snapshot-miss-vägen inte fångat dem.
/// </para>
/// </summary>
public sealed class ExpireJobAdsWorker(ExpireJobAdsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
