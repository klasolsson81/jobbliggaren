using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Application.JobAds.Jobs.BackfillJobAdSsyk;

/// <summary>
/// STEG 6 (2026-05-24) — engångs-backfill av <c>ssyk_concept_id</c> för JobAds
/// vars <c>raw_payload</c> saknar <c>occupation</c>-key (pre-2026-05-20-
/// <c>JobTechHit.Occupation</c>-fix). Re-hämtar per <c>External.ExternalId</c>
/// mot JobTech <c>jobsearch.api.jobtechdev.se/ad/{id}</c> via
/// <see cref="IJobSource.RefetchByExternalIdAsync"/> och kör samma
/// <see cref="UpsertExternalJobAdCommand"/>-pipeline som
/// <c>SyncPlatsbankenSnapshotJob</c> — UNIQUE-collision triggar UPDATE,
/// raw_payload re-skrivs, Postgres STORED computed column re-evaluerar.
///
/// <para>
/// <b>Varför inte snapshot-trigger:</b> JobTech <c>/v2/snapshot</c> trunkerar
/// icke-deterministiskt vid ~10k rader (CloudWatch-evidens 2026-05-22→24).
/// 35k legacy-rader fastnar utanför trunkerad prefix. Per-ID-fetch ger
/// deterministisk coverage. ADR 0032-amendment 2026-05-16 bounded retry-
/// disciplin bevaras intakt (architect-rond 2026-05-24).
/// </para>
///
/// <para>
/// <b>404-semantik:</b> Annons borttagen från källan → skip + log + count.
/// INGEN arkivering eller miss-tracking-påverkan — det ägs av snapshot-flödet
/// (ADR 0032-amendment 2026-05-23).
/// </para>
///
/// <para>
/// <b>Race-säkerhet:</b> filter på <c>ssyk_concept_id IS NULL</c> kan se
/// "extra" rader om snapshot kör samtidigt (snapshot kl 02:00 UTC). Worst case
/// = onödig JobTech-trafik (no-op-overhead, idempotent via UNIQUE-index +
/// <see cref="JobAd.UpdateFromSource"/>). Acceptabelt; Klas väljer tidpunkt.
/// </para>
///
/// <para>
/// <b>Hangfire-mönster:</b> Fire-and-forget via
/// <c>IBackgroundJobClient.Enqueue&lt;BackfillJobAdSsykWorker&gt;</c>. INTE
/// registrerad i <c>RecurringJobRegistrar</c> (engångs-operation).
/// <c>[DisableConcurrentExecution(7200)]</c> tillämpas på Worker-wrappern
/// (Worker-lagret, samma mönster som <c>SyncPlatsbankenSnapshotWorker</c> —
/// Hangfire-attribut får inte läcka till Application).
/// </para>
/// </summary>
public sealed partial class BackfillJobAdSsykJob(
    IJobSource jobSource,
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IOptions<BackfillJobAdSsykOptions> options,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<BackfillJobAdSsykJob> logger)
{
    public async Task<BackfillCounts> RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var opts = options.Value;
        LogStarted(logger, jobSource.Source.Value, opts.PerItemDelayMs, opts.MaxItemsPerRun);

        var counts = new BackfillCounts { StartedAt = startedAt };

        // Streamar via AsAsyncEnumerable — materialiserar ALDRIG hela ~35k-listan
        // i minnet. ADR 0045 Beslut 3 Worker-mem soft cap 512 MiB bevaras.
        // OrderBy(ExternalId) ger deterministisk iteration-ordning för upprepade
        // körningar (idempotent restart-mönster). ExternalId är string — sorteras
        // korrekt både i Npgsql och InMemory-provider (JobAdId-VO saknar
        // IComparable och stupar i InMemory:s default-comparer).
        var externalIdQuery = db.JobAds
            .Where(j => EF.Property<string?>(j, "SsykConceptId") == null
                        && j.External != null
                        && j.External.Source == jobSource.Source)
            .OrderBy(j => j.External!.ExternalId)
            .Select(j => j.External!.ExternalId)
            .AsNoTracking()
            .AsAsyncEnumerable();

        var perItemDelay = TimeSpan.FromMilliseconds(opts.PerItemDelayMs);

        await foreach (var externalId in externalIdQuery.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Fetched++;

            if (counts.Fetched > opts.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, opts.MaxItemsPerRun);
                break;
            }

            try
            {
                counts.RefetchAttempted++;
                var refetched = await jobSource.RefetchByExternalIdAsync(
                    externalId, cancellationToken);

                if (refetched is null)
                {
                    counts.NotFoundOnSource++;
                    continue;
                }

                // Egen DI-scope per item (samma mönster som SyncPlatsbankenSnapshotJob
                // line 76-93). Återställer ADR 0032 §5 single-command-scope-
                // antagandet — change-tracker lever och dör med ETT item.
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var mediator = itemScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new UpsertExternalJobAdCommand(jobSource.Source, externalId, refetched);
                var result = await mediator.Send(cmd, cancellationToken);

                if (result.IsFailure)
                {
                    counts.Errors++;
                    continue;
                }

                // code-reviewer M-1 (2026-05-24): diskriminera UpsertOutcome.
                // Handlern returnerar Added/Updated/Skipped (success-grenar) —
                // sammanslagning till en räknare ljuger om backfill-progress
                // (en archived annons → handler returnerar Skipped, inte fel).
                switch (result.Value)
                {
                    case UpsertOutcome.Added: counts.Added++; break;
                    case UpsertOutcome.Updated: counts.Updated++; break;
                    case UpsertOutcome.Skipped: counts.SkippedByHandler++; break;
                }

                if (counts.RefetchAttempted % opts.ProgressLogEvery == 0)
                    LogProgress(logger, counts.RefetchAttempted, counts.Updated,
                        counts.NotFoundOnSource, counts.Errors);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.Errors++;
                LogItemFailed(logger, ex, externalId);
            }

            // Sekventiell throttle. CancellationToken propageras → mid-run abort
            // via Hangfire-dashboard fungerar. ADR 0032-amendment-disciplin
            // (bounded ops mot JobTech) respekteras explicit istället för Polly-
            // queue-buildup.
            if (perItemDelay > TimeSpan.Zero)
                await Task.Delay(perItemDelay, cancellationToken);
        }

        var completedAt = clock.UtcNow;
        counts.CompletedAt = completedAt;

        LogCompleted(logger, jobSource.Source.Value, counts.Fetched, counts.Updated,
            counts.NotFoundOnSource, counts.Errors, (completedAt - startedAt).TotalSeconds);

        // Audit-wire — återanvänder JobAdsSynced med JobType="backfill" (architect-
        // rond 2026-05-24: inget nytt audit-koncept under MVP-press).
        // Skipped-fältet i audit-eventet = handlerns Skipped + NotFoundOnSource (källa-borta)
        // — båda är "rad iteret men ej uppdaterad" ur retention-pipelinen-vy.
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: jobSource.Source.Value,
            JobType: "backfill",
            Fetched: counts.Fetched,
            Added: counts.Added,
            Updated: counts.Updated,
            Archived: 0,
            Skipped: counts.SkippedByHandler + counts.NotFoundOnSource,
            Errors: counts.Errors,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken);

        return counts;
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information,
        Message = "BackfillJobAdSsykJob: startad — source={Source}, perItemDelayMs={Delay}, maxItemsPerRun={Max}.")]
    private static partial void LogStarted(ILogger logger, string source, int delay, int max);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Information,
        Message = "BackfillJobAdSsykJob: progress — refetchAttempted={Attempted}, updated={Updated}, notFound={NotFound}, errors={Errors}.")]
    private static partial void LogProgress(ILogger logger, int attempted, int updated, int notFound, int errors);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Information,
        Message = "BackfillJobAdSsykJob: klart — source={Source}, fetched={Fetched}, updated={Updated}, notFound={NotFound}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, string source, int fetched,
        int updated, int notFound, int errors, double durationSec);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Warning,
        Message = "BackfillJobAdSsykJob: item-failure ExternalId={ExternalId} — räknas i Errors, fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, string externalId);

    [LoggerMessage(EventId = 6005, Level = LogLevel.Warning,
        Message = "BackfillJobAdSsykJob: MaxItemsPerRun={MaxItems} nådd — bryter och avslutar gracefully. Idempotent: nästa körning plockar resterande NULL-rader.")]
    private static partial void LogMaxItemsReached(ILogger logger, int maxItems);
}

/// <summary>
/// Aggregerad statistik från backfill-run. Loggas + returneras (om någon konsument vill se).
/// Mutable by design — jobbet är ensam writer (per-iteration-counter-aggregering).
/// </summary>
public sealed class BackfillCounts
{
    public int Fetched { get; set; }              // antal NULL-rader sett (= input)
    public int RefetchAttempted { get; set; }     // GET mot JobTech /ad/{id}
    public int Added { get; set; }                // UpsertExternalJobAdCommand → Added (sällsynt: rad försvann mellan IS-NULL-query och INSERT)
    public int Updated { get; set; }              // UpsertExternalJobAdCommand → Updated (normal-fallet)
    public int SkippedByHandler { get; set; }     // UpsertExternalJobAdCommand → Skipped (validering / archived / no-change)
    public int NotFoundOnSource { get; set; }     // 404 från JobTech
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
