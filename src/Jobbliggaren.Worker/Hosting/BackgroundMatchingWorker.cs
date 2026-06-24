using Hangfire;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="BackgroundMatchingJob"/> (ADR 0080 Vag 4 PR-3) som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-beroende
/// till Application-lagret (Clean Arch — ADR 0023 delbeslut 2; paritet <see cref="ExpireJobAdsWorker"/>).
/// <para>
/// Timeout 1800 sekunder (30 min). Den dagliga per-user matchnings-scannen kan vara långkörande
/// (N samtyckande användare × nya annonser sedan vattenmärket) och får ALDRIG överlappa sig själv:
/// en AutomaticRetry-overlap skulle dubbel-skanna mot samma <c>LastMatchScanAt</c>-vattenmärke
/// innan det avancerats (idempotensen vilar på att watermark-avancemanget commit:as atomiskt).
/// </para>
/// </summary>
public sealed class BackgroundMatchingWorker(BackgroundMatchingJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
