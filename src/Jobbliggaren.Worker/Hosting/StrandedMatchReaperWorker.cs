using Hangfire;
using Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="StrandedMatchReaperJob"/> (TD-114, ADR 0080 Vag 4) som
/// applicerar Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroendet till Application-lagret (Clean Arch — ADR 0023 delbeslut 2; paritet
/// <see cref="BackgroundMatchingWorker"/>).
/// <para>
/// Timeout 1800 sekunder (30 min). Reaper-svepet är litet (endast matchningar som fastnat i
/// Queued past tröskeln, normalt en handfull) men får inte överlappa sig självt — en överlapp
/// skulle dubbel-bearbeta samma strandade rader (ofarligt eftersom MarkFailed är idempotent,
/// men onödigt).
/// </para>
/// </summary>
public sealed class StrandedMatchReaperWorker(StrandedMatchReaperJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
