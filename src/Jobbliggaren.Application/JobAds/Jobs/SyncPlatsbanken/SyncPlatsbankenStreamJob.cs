using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;

/// <summary>
/// Hangfire RecurringJob (cron <c>*/10 * * * *</c> per ADR 0032 §3). Hämtar
/// inkrementella ändringar från JobTech JobStream och delegerar per event till
/// <see cref="UpsertExternalJobAdCommand"/> eller <see cref="ArchiveExternalJobAdCommand"/>
/// via Mediator. Aggregerad sync-statistik loggas strukturerat (audit-wire till
/// <c>audit_log.payload</c> defereras till TD-73 right-to-erasure-batch per
/// senior-cto-advisor 2026-05-13 punkt 5).
///
/// <para>
/// <b>Cursor-state via overlap-window (ADR 0032 §3 + CTO-rond 2026-05-13 punkt 2):</b>
/// Eftersom UNIQUE-indexet + <c>UpdateFromSource</c> gör upserten idempotent
/// håller vi ingen cursor-tabell. Stream-cron körs var 10:e min och frågar med
/// <c>since = now - 15 min</c> (5 min overlap). Tappade kör tolereras
/// (Fowler 2002 "Idempotent Receiver"). Snapshot 02:00 fångar drift.
/// </para>
///
/// <para>
/// <b>Concurrency:</b> Worker är single-instance i Fas 2 (Fargate 1 task).
/// Defense-in-depth mot framtida horisontell skalning sker via
/// <c>[DisableConcurrentExecution]</c>-attribut på Worker-wrapper-klass
/// (<c>SyncPlatsbankenStreamWorker</c>) — Application-lagret hålls
/// Hangfire-fritt per Clean Arch.
/// </para>
///
/// <para>
/// <b>Per-event isolering:</b> En misslyckad upsert/archive ska inte fälla hela
/// batch:n. Try/catch per event räknar i ErrorCount. ADR 0032 §3 +
/// HardDeleteAccountsJob TD-25-pattern.
/// </para>
/// </summary>
public sealed partial class SyncPlatsbankenStreamJob(
    IJobSource jobSource,
    IMediator mediator,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    IngestionThroughputReporter throughputReporter,
    ILogger<SyncPlatsbankenStreamJob> logger)
{
    // 5 min overlap utöver 10-min cron-cykel. Upserts är idempotenta via UNIQUE-index.
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromMinutes(15);

    // Ett namn, ett ställe (§5 magic strings). Audit-raden och throughput-eventet MÅSTE bära
    // samma jobType — runbook §B korrelerar dem mot varandra i Seq.
    private const string JobTypeName = "stream";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Per-run-Guid för audit-rad — bevarar AggregateId-invarianten (non-Empty)
        // och länkar framtida started+completed-events i samma run (ADR 0035 §2).
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var since = startedAt - OverlapWindow;

        LogStarted(logger, jobSource.Source.Value, since);

        // Sätts först när foreach:en fullbordats normalt. Läses i `finally` så en LYCKAD körning
        // får EN enda completion-instans — LogCompleted, audit-raden och throughput-eventet
        // rapporterar då samma durationSec (två klockläsningar hade rapporterat marginellt olika
        // tal för samma körning, och runbook §C ber folk stämma av dem mot varandra).
        DateTimeOffset? succeededAt = null;

        var fetched = 0;
        var added = 0;
        var updated = 0;
        var archived = 0;
        var skipped = 0;
        var errors = 0;

        try
        {
            await foreach (var change in jobSource.StreamChangesAsync(since, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                fetched++;

                try
                {
                    switch (change)
                    {
                        case JobAdUpsert upsert:
                            {
                                var cmd = new UpsertExternalJobAdCommand(
                                    jobSource.Source, upsert.ExternalId, upsert.Item);
                                var result = await mediator.Send(cmd, cancellationToken);
                                if (result.IsFailure) { errors++; break; }
                                switch (result.Value)
                                {
                                    case UpsertOutcome.Added: added++; break;
                                    case UpsertOutcome.Updated: updated++; break;
                                    case UpsertOutcome.Skipped: skipped++; break;
                                }
                                break;
                            }
                        case JobAdRemoval removal:
                            {
                                var cmd = new ArchiveExternalJobAdCommand(
                                    jobSource.Source, removal.ExternalId);
                                var result = await mediator.Send(cmd, cancellationToken);
                                if (result.IsFailure) { errors++; break; }
                                switch (result.Value)
                                {
                                    case ArchiveOutcome.Archived: archived++; break;
                                    case ArchiveOutcome.AlreadyArchived: skipped++; break;
                                    case ArchiveOutcome.NotFound: skipped++; break;
                                }
                                break;
                            }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // TD-25-mönster: isolerad failure stoppar inte hela batch:en.
                    errors++;
                    LogEventFailed(logger, ex, change.ExternalId);
                }
            }

            // CTO bind #754 Q3 (ADR 0045 Beslut 1 klass (d)) — throughput
            // verdict only for a run that completed the foreach normally.
            // Deliberately NOT in `finally`: a crashed run has no valid
            // capacity claim — a partial run would compute a bogus low rate
            // and warn about capacity when the real event was a failure
            // (already logged at Error by LogEventFailed / the OCE rethrow
            // below propagating out of RunAsync entirely).
            succeededAt = clock.UtcNow;
            throughputReporter.Report(
                jobSource.Source.Value, JobTypeName, fetched,
                (succeededAt.Value - startedAt).TotalSeconds);
        }
        finally
        {
            var completedAt = succeededAt ?? clock.UtcNow;
            LogCompleted(logger, jobSource.Source.Value, fetched, added, updated,
                archived, skipped, errors, (completedAt - startedAt).TotalSeconds);

            // Audit-wire α (ADR 0035 + ADR 0032 §8 amendment 2026-05-13).
            // SystemEventAuditor är idempotent vid Hangfire-retry via per-runId-
            // lookup. Try/catch här bevarar originalexception (Cwalina/Abrams
            // 2008 §7.5 — "finally" får inte maska try-blockets exception).
            // Audit-failure loggas Critical inom auditor:n, exception svaljs
            // här för att inte skugga sync-failure. Hangfire-retry kör om hela
            // jobbet vid sync-fel; idempotens-checken hindrar duplicate audit.
            try
            {
                await auditor.RecordAsync(new JobAdsSynced(
                    AggregateId: runId,
                    OccurredAt: completedAt,
                    Source: jobSource.Source.Value,
                    JobType: JobTypeName,
                    Fetched: fetched,
                    Added: added,
                    Updated: updated,
                    Archived: archived,
                    Skipped: skipped,
                    Errors: errors,
                    StartedAt: startedAt,
                    CompletedAt: completedAt), cancellationToken);
            }
#pragma warning disable CA1031 // medvetet swallow för att inte maska originalexception (Cwalina/Abrams §7.5)
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Audit-failure har redan Critical-loggats inom SystemEventAuditor.
                // Svälj här (med CA1031-suppression) för att inte maska
                // originalexception från try-blocket. CA2219 förbjuder throw från
                // finally — semantiken är: sync-failure (try) bubblar med korrekt
                // stack-trace; audit-failure noteras i Critical-log men maskar inte
                // sync-felet.
            }
#pragma warning restore CA1031
        }
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Information,
        Message = "SyncPlatsbankenStreamJob: startad — source={Source}, since={Since:O}.")]
    private static partial void LogStarted(ILogger logger, string source, DateTimeOffset since);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information,
        Message = "SyncPlatsbankenStreamJob: klart — source={Source}, fetched={Fetched}, added={Added}, updated={Updated}, archived={Archived}, skipped={Skipped}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, string source, int fetched,
        int added, int updated, int archived, int skipped, int errors, double durationSec);

    // event_name=-konvention per ADR 0031 (FailedAccessLogger) + ADR 0036
    // (cloudwatch_ops_alarms-modul matchar metric filter mot detta prefix).
    [LoggerMessage(EventId = 5303, Level = LogLevel.Warning,
        Message = "event_name=job_event_failure job_name=SyncPlatsbankenStreamJob external_id={ExternalId} — räknas i ErrorCount, fortsätter med nästa.")]
    private static partial void LogEventFailed(ILogger logger, Exception exception, string externalId);
}
