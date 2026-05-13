using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.ArchiveExternalJobAd;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;

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
    ILogger<SyncPlatsbankenStreamJob> logger)
{
    // 5 min overlap utöver 10-min cron-cykel. Upserts är idempotenta via UNIQUE-index.
    private static readonly TimeSpan OverlapWindow = TimeSpan.FromMinutes(15);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        var since = startedAt - OverlapWindow;

        LogStarted(logger, jobSource.Source.Value, since);

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
        }
        finally
        {
            var completedAt = clock.UtcNow;
            LogCompleted(logger, jobSource.Source.Value, fetched, added, updated,
                archived, skipped, errors, (completedAt - startedAt).TotalSeconds);
        }
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Information,
        Message = "SyncPlatsbankenStreamJob: startad — source={Source}, since={Since:O}.")]
    private static partial void LogStarted(ILogger logger, string source, DateTimeOffset since);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information,
        Message = "SyncPlatsbankenStreamJob: klart — source={Source}, fetched={Fetched}, added={Added}, updated={Updated}, archived={Archived}, skipped={Skipped}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, string source, int fetched,
        int added, int updated, int archived, int skipped, int errors, double durationSec);

    [LoggerMessage(EventId = 5303, Level = LogLevel.Warning,
        Message = "SyncPlatsbankenStreamJob: event-failure ExternalId={ExternalId} — räknas i ErrorCount, fortsätter med nästa.")]
    private static partial void LogEventFailed(ILogger logger, Exception exception, string externalId);
}
