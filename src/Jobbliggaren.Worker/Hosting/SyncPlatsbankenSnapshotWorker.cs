using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="SyncPlatsbankenSnapshotJob"/> som applicerar
/// Hangfire <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka
/// Hangfire-beroende till Application-lagret (Clean Arch — ADR 0023 delbeslut 2,
/// samma mönster som <see cref="SyncPlatsbankenStreamWorker"/>).
///
/// <para>
/// Timeout 3600 sekunder (1 h) är väl över worst-case snapshot-tid: efter
/// streaming-fixen (2026-05-16) itereras hela ~47k-korpusen med en child-scope
/// per item, vilket tar tiotals minuter. Utan <c>DisableConcurrentExecution</c>
/// kan Hangfire <c>AutomaticRetry</c> återskapa den överlappnings-loop som var
/// en del av root-cause-symptomen (60 starts / 0 completes). Recurring-jobb-id
/// <c>sync-platsbanken-snapshot</c> är oförändrat → manuell "Trigger now" i
/// Hangfire-dashboarden fungerar (ersätter den avvecklade admin-endpointen,
/// ADR 0032 §9-amendment 2026-05-16).
/// </para>
/// </summary>
public sealed class SyncPlatsbankenSnapshotWorker(SyncPlatsbankenSnapshotJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
