using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — Worker-wrapper för
/// <see cref="BackfillJobAdRequirementsJob"/> som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-
/// beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2, samma
/// mönster som <see cref="BackfillJobAdKlass2Worker"/>).
///
/// <para>
/// Timeout 14400 s (4 h): F4-4b re-hämtar HELA tabellen (~54k rader saknar
/// <c>must_have</c> tills körningen skett, till skillnad mot Klass2:s ~21% NULL), så
/// vid default <c>PerItemDelayMs=200</c> tar körningen ~3h plus per-item child-scope +
/// Mediator-pipeline + transient retry-overhead. Idempotent — avbruten körning
/// plockas upp av nästa enqueue (<c>must_have</c>-nyckel-filtret är race-säkert mot
/// snapshot-cron).
/// </para>
/// </summary>
public sealed class BackfillJobAdRequirementsWorker(BackfillJobAdRequirementsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 14400)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        job.RunAsync(cancellationToken);
}
