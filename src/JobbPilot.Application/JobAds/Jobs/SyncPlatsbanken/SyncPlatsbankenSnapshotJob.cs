using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Domain.Common;
using Mediator;
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
/// Per CTO-rond 2026-05-13 punkt 3: Snapshot-orchestrator + admin-trigger
/// delar samma per-item-Mediator-kodväg. <c>SyncPlatsbankenSnapshotCommand</c>
/// (P8b admin-endpoint) refaktoreras i samma batch att kalla denna job-impl
/// så vi inte har två sync-flöden.
/// </para>
/// </summary>
public sealed partial class SyncPlatsbankenSnapshotJob(
    IJobSource jobSource,
    IMediator mediator,
    IDateTimeProvider clock,
    ILogger<SyncPlatsbankenSnapshotJob> logger)
{
    public async Task<SyncCounts> RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;
        LogStarted(logger, jobSource.Source.Value);

        var snapshot = await jobSource.FetchSnapshotAsync(cancellationToken);

        var counts = new SyncCounts { Fetched = snapshot.Items.Count };

        foreach (var item in snapshot.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
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
