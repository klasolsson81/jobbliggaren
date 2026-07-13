using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken;

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
    IJobAdSnapshotMissTracker missTracker,
    IOptions<JobSourceRetentionOptions> retentionOptions,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    IngestionThroughputReporter throughputReporter,
    ILogger<SyncPlatsbankenSnapshotJob> logger)
{
    // Ett namn, ett ställe (§5 magic strings). Audit-raden och throughput-eventet MÅSTE bära
    // samma jobType — runbook §B korrelerar dem mot varandra i Seq.
    private const string JobTypeName = "snapshot";

    public async Task<SyncCounts> RunAsync(CancellationToken cancellationToken)
    {
        // Per-run-Guid för audit-rad (ADR 0035 §2).
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        LogStarted(logger, jobSource.Source.Value);

        var counts = new SyncCounts();
        // ADR 0032-amendment 2026-05-23: håll set av ExternalIds som sågs i
        // denna run för efter-foreach-miss-tracking. ~50k strängar à ~30B ≈
        // 1.5 MB — acceptabelt mot ADR 0045 Worker minnesbudget. Capacity
        // pre-allokeras så ingen rehash sker mid-run.
        var seenIds = new HashSet<string>(capacity: 60_000, StringComparer.Ordinal);
        var outcome = new SnapshotOutcomeRecorder();

        // Strömmas per item (IAsyncEnumerable) — hela ~300 MB-snapshot
        // materialiseras aldrig (root-cause-fix 2026-05-16, del (a)).
        await foreach (var item in jobSource.FetchSnapshotAsync(outcome, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Fetched++;
            seenIds.Add(item.ExternalId);

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

        // ADR 0032-amendment 2026-05-23: miss-tracking endast vid komplett
        // snapshot (icke-trunkerad). Vid trunkering kan vi inte särskilja
        // "saknad i källan" från "saknad i denna trunkerade prefix" → skippa.
        // Snapshot-jobbet skriver INTE miss-tabellen vid trunkering → retention-
        // jobbet (separat cron 03:15) skannar oförändrad tabell → ingen falsk
        // arkivering.
        var snapshotOutcome = outcome.Outcome
            ?? throw new InvalidOperationException(
                "JobSource avslutade utan att registrera SnapshotOutcome — kontrakt-brott.");

        // CTO-rond 2026-05-23 Q5 — defense-in-depth floor-skydd mot mass-felaktig
        // arkivering. Tre obergande villkor måste alla passera innan miss-tracking
        // får påverka miss-tabellen:
        //   (1) Snapshot ej trunkerad
        //   (2) ParsedTotal >= SnapshotAbsoluteFloor (30 000 default)
        //   (3) ParsedTotal >= max_7d × SnapshotRelativeFloorRatio (0.80 default)
        // Vid floor-brott skippas miss-tracking (max_7d-baslinjen försämras ej
        // heller) → retention-jobbet skannar oförändrad tabell → ingen falsk
        // arkivering kan inträffa.
        var opts = retentionOptions.Value;
        var max7d = await missTracker.GetMaxObservedSnapshotSizeAsync(
            jobSource.Source, days: 7, cancellationToken);
        var absoluteFloorViolated = snapshotOutcome.ParsedTotal < opts.SnapshotAbsoluteFloor;
        var relativeFloorViolated = max7d.HasValue
            && snapshotOutcome.ParsedTotal < max7d.Value * opts.SnapshotRelativeFloorRatio;

        if (snapshotOutcome.TruncatedAndExhausted)
        {
            LogMissTrackingSkippedDueToTruncation(
                logger, jobSource.Source.Value, snapshotOutcome.ParsedTotal, snapshotOutcome.Attempts);
        }
        else if (absoluteFloorViolated)
        {
            LogMissTrackingSkippedDueToAbsoluteFloor(
                logger, jobSource.Source.Value, snapshotOutcome.ParsedTotal, opts.SnapshotAbsoluteFloor);
        }
        else if (relativeFloorViolated)
        {
            LogMissTrackingSkippedDueToRelativeFloor(
                logger, jobSource.Source.Value, snapshotOutcome.ParsedTotal,
                max7d!.Value, opts.SnapshotRelativeFloorRatio);
        }
        else
        {
            var missResult = await missTracker.ApplyAsync(
                jobSource.Source, seenIds, completedAt, cancellationToken);
            counts.MissTrackingApplied = true;
            counts.MissResetCount = missResult.ResetCount;
            counts.MissIncrementedCount = missResult.IncrementedCount;
        }

        LogCompleted(logger, jobSource.Source.Value, counts.Fetched, counts.Added,
            counts.Updated, counts.Skipped, counts.Errors,
            (completedAt - startedAt).TotalSeconds);

        // CTO bind #754 Q3 (ADR 0045 Beslut 1 klass (d)) — this point is only
        // reached on a run that completed the foreach normally (no `finally`,
        // no earlier `return`, above this line for job-level failure) —
        // naturally correct placement, no extra guard needed. Reuses the
        // already-computed startedAt/completedAt — no additional clock read.
        throughputReporter.Report(
            jobSource.Source.Value, JobTypeName, counts.Fetched,
            (completedAt - startedAt).TotalSeconds);

        // Audit-wire α (ADR 0035 + ADR 0032 §8 amendment 2026-05-13).
        // En audit-rad per snapshot-run oavsett om trigger är admin-curl
        // eller nattlig cron (M3 — SyncPlatsbankenSnapshotCommand har inte
        // IAuditableCommand-marker).
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: jobSource.Source.Value,
            JobType: JobTypeName,
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

    [LoggerMessage(EventId = 5404, Level = LogLevel.Warning,
        Message = "SyncPlatsbankenSnapshotJob: miss-tracking SKIPPAD pga trunkerad snapshot — source={Source}, parsed={ParsedTotal}, attempts={Attempts}. Retention-jobbet skannar oförändrad miss-tabell → ingen falsk arkivering. ADR 0032-amendment 2026-05-23.")]
    private static partial void LogMissTrackingSkippedDueToTruncation(
        ILogger logger, string source, int parsedTotal, int attempts);

    [LoggerMessage(EventId = 5405, Level = LogLevel.Warning,
        Message = "SyncPlatsbankenSnapshotJob: miss-tracking SKIPPAD pga absolut floor — source={Source}, parsed={ParsedTotal} < {AbsoluteFloor}. CTO-rond 2026-05-23 Q5. Ingen falsk arkivering.")]
    private static partial void LogMissTrackingSkippedDueToAbsoluteFloor(
        ILogger logger, string source, int parsedTotal, int absoluteFloor);

    [LoggerMessage(EventId = 5406, Level = LogLevel.Warning,
        Message = "SyncPlatsbankenSnapshotJob: miss-tracking SKIPPAD pga relativ floor — source={Source}, parsed={ParsedTotal} < {Max7d} × {Ratio}. CTO-rond 2026-05-23 Q5.")]
    private static partial void LogMissTrackingSkippedDueToRelativeFloor(
        ILogger logger, string source, int parsedTotal, int max7d, double ratio);
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

    // ADR 0032-amendment 2026-05-23: miss-tracking-resultat. Defaultar false
    // för backwards-compat med befintliga konsumenter (admin-endpoint, tester).
    public bool MissTrackingApplied { get; set; }
    public int MissResetCount { get; set; }
    public int MissIncrementedCount { get; set; }
}
