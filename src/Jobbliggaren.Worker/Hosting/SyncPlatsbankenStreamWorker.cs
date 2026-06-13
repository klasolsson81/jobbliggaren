using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="SyncPlatsbankenStreamJob"/> som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2).
///
/// <para>
/// Timeout 540 sekunder är 9 minuter — kortare än 10-min cron-cykel så
/// Hangfire alltid hinner force-skipa nästa invocation om föregående hänger.
/// Skydd mot horisontell skalning (multipla Worker-instanser) som inte är
/// aktuellt i Fas 2 men gratis defense-in-depth (CTO-rond 2026-05-13 punkt 8).
/// </para>
/// </summary>
public sealed class SyncPlatsbankenStreamWorker(SyncPlatsbankenStreamJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 540)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
