using Hangfire;
using JobbPilot.Application.Landing.Jobs.RefreshLandingStats;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="RefreshLandingStatsJob"/> som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023
/// delbeslut 2; paritet <see cref="ExpireJobAdsWorker"/>).
///
/// <para>
/// Timeout 60 sekunder: två COUNT-queries (Active + NewToday) på ~46k
/// indexerade rader + en Redis SET tar sub-sekund typiskt. 60s täcker
/// worst-case DB-lock-väntning utan att blockera nästa <c>*/5</c>-tick.
/// </para>
/// </summary>
public sealed class RefreshLandingStatsWorker(RefreshLandingStatsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
