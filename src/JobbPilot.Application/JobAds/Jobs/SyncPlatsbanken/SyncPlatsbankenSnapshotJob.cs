using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;

/// <summary>
/// Hangfire RecurringJob (cron <c>0 2 * * *</c> per ADR 0032 §3). Daglig
/// fullbackfill mot Stream-event-drift. Hämtar hela snapshot från
/// <see cref="IJobSource.FetchSnapshotAsync"/> och delegerar per item till
/// <see cref="UpsertExternalJobAdCommand"/> via Mediator (race-säker via
/// UNIQUE-index per ADR 0032 §5).
///
/// <para>
/// <b>Child-scope per item (root-cause-fix 2026-05-16, senior-cto-advisor
/// Variant B):</b> Snapshot:en (~47k items) strömmas och VARJE item upsertas
/// i en egen DI-scope (<see cref="IServiceScopeFactory.CreateAsyncScope"/>).
/// Skäl: hela loopen körde tidigare i en enda scope → ett scoped
/// <c>IAppDbContext</c> vars change-tracker ackumulerade över alla items.
/// <c>UnitOfWorkBehavior</c> kör en SaveChanges efter varje
/// <c>mediator.Send</c> över hela den ackumulerade grafen — när snapshot ⊇
/// det stream redan infogat (tusentals dubbletter) bröt det
/// <see cref="UpsertExternalJobAdCommandHandler"/>:s per-command
/// DbUpdateException-isolering (ADR 0032 §5 antar single-command-scope per
/// upsert). Resultat: uncaught 23505 → Hangfire-retry-loop, 60 starts /
/// 0 completes på dev. Child-scope återställer §5:s scope-antagande.
/// </para>
///
/// <para>
/// Per CTO-rond 2026-05-13 punkt 3 + 2026-05-16: admin-trigger delar samma
/// recurring-jobb via Hangfire-enqueue (inte längre synkron Mediator-shim).
/// </para>
/// </summary>
public sealed partial class SyncPlatsbankenSnapshotJob(
    IJobSource jobSource,
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<SyncPlatsbankenSnapshotJob> logger)
{
    public async Task<SyncCounts> RunAsync(CancellationToken cancellationToken)
    {
        // Per-run-Guid för audit-rad (ADR 0035 §2).
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        LogStarted(logger, jobSource.Source.Value);

        var counts = new SyncCounts();

        // Strömmas per item (IAsyncEnumerable) — hela ~300 MB-snapshot
        // materialiseras aldrig (root-cause-fix 2026-05-16, del (a)).
        await foreach (var item in jobSource.FetchSnapshotAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Fetched++;

            try
            {
                // Egen DI-scope per item → eget IAppDbContext → change-tracker
                // lever och dör med ETT item. Återställer ADR 0032 §5:s
                // single-command-scope-antagande vid 47k-batch-skala.
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var mediator = itemScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new UpsertExternalJobAdCommand(jobSource.Source, item.ExternalId, item);
                var result = await mediator.Send(cmd, cancellationToken);

                if (result.IsFailure)
                {
                    counts.Errors++;
                    continue;
                }

                switch (result.Value)
                {
                    case UpsertOutcome.Added: counts.Added++; break;
                    case UpsertOutcome.Updated: counts.Updated++; break;
                    case UpsertOutcome.Skipped: counts.Skipped++; break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.Errors++;
                LogItemFailed(logger, ex, item.ExternalId);
            }
        }

        var completedAt = clock.UtcNow;
        counts.StartedAt = startedAt;
        counts.CompletedAt = completedAt;

        LogCompleted(logger, jobSource.Source.Value, counts.Fetched, counts.Added,
            counts.Updated, counts.Skipped, counts.Errors,
            (completedAt - startedAt).TotalSeconds);

        // Audit-wire α (ADR 0035 + ADR 0032 §8 amendment 2026-05-13).
        // En audit-rad per snapshot-run oavsett om trigger är admin-curl
        // eller nattlig cron (M3 — SyncPlatsbankenSnapshotCommand har inte
        // IAuditableCommand-marker).
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: jobSource.Source.Value,
            JobType: "snapshot",
            Fetched: counts.Fetched,
            Added: counts.Added,
            Updated: counts.Updated,
            Archived: 0,  // snapshot rör inte archive-flödet
            Skipped: counts.Skipped,
            Errors: counts.Errors,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken);

        return counts;
    }

    [LoggerMessage(EventId = 5401, Level = LogLevel.Information,
        Message = "SyncPlatsbankenSnapshotJob: startad — source={Source}.")]
    private static partial void LogStarted(ILogger logger, string source);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Information,
        Message = "SyncPlatsbankenSnapshotJob: klart — source={Source}, fetched={Fetched}, added={Added}, updated={Updated}, skipped={Skipped}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, string source, int fetched,
        int added, int updated, int skipped, int errors, double durationSec);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Warning,
        Message = "SyncPlatsbankenSnapshotJob: item-failure ExternalId={ExternalId} — räknas i ErrorCount, fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, string externalId);
}

/// <summary>Aggregerad statistik från snapshot-jobb-run. Returneras till callers (admin-endpoint).</summary>
public sealed class SyncCounts
{
    public int Fetched { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
