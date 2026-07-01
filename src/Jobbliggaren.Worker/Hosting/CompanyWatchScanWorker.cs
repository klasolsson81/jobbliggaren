using Hangfire;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="CompanyWatchScanJob"/> (ADR 0087 D5, #311 PR-4) som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-beroende till
/// Application-lagret (Clean Arch — ADR 0023 delbeslut 2; paritet <see cref="BackgroundMatchingWorker"/>).
/// <para>
/// Timeout 1800 sekunder (30 min). Den nattliga företagsföljnings-scannen får ALDRIG överlappa sig
/// själv: en AutomaticRetry-overlap skulle dubbel-skanna mot samma
/// <c>LastCompanyWatchScanAt</c>-vattenmärke innan det avancerats (idempotensen vilar på att
/// watermark-avancemanget commit:as atomiskt).
/// </para>
/// </summary>
public sealed class CompanyWatchScanWorker(CompanyWatchScanJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
