using System.Linq.Expressions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Jobs.Common;

/// <summary>
/// Delad re-ingest-kärna för engångs-backfill av STORED generated columns vars
/// <c>raw_payload</c> saknar en taxonomi-key (POCO deserialiserade den inte vid
/// importtillfället). Extraherad 2026-06-08 (senior-cto-advisor Variant H) ur
/// <c>BackfillJobAdSsykJob</c> när Fas B2 (Klass 2: employment_type +
/// worktime_extent) behövde EXAKT samma mekanik med ett annat NULL-kolumn-
/// predikat — DRY på knowledge-piece-nivå (Hunt/Thomas 1999 kap. 7) + OCP
/// (Martin 2017 kap. 8): nytt backfill-behov = ny tunn wrapper + nytt predikat,
/// kärnan rörs ej.
///
/// <para>
/// <b>Mekanik:</b> strömmar <c>External.ExternalId</c> för rader som matchar
/// <paramref name="nullColumnPredicate"/> (typiskt "shadow-kolumn IS NULL") och
/// tillhör <see cref="IJobSource.Source"/>, re-hämtar var och en via
/// <see cref="IJobSource.RefetchByExternalIdAsync"/> (JobTech <c>/ad/{id}</c>,
/// deterministisk per-ID — undviker snapshot-trunkering, ADR 0032-amendment
/// 2026-05-16) och kör samma <see cref="UpsertExternalJobAdCommand"/>-pipeline
/// som snapshot-jobbet. UNIQUE-collision triggar UPDATE, raw_payload re-skrivs
/// HELT → och sedan #841 skriver ingest-funneln OM de sju facett-kolumnerna i C#
/// (JobAd.SetSourcePayload, atomärt med payloaden). Det är precis DÄRFÖR den här
/// runnern är #841:s DEPLOY-REPARATIONSVERKTYG: en annons vars facetter nollats av
/// purgen matchar NULL-predikatet, re-fetchas, och får alla sju skrivna på nytt.
/// (Före #841 stod här "Postgres STORED computed columns re-evaluerar" — den meningen
/// förklarade varför reparationen fungerade, och den blev falsk i samma PR som gjorde
/// reparationen nödvändig. En annons som LÄMNAT flödet ger 404 och hoppas över; dess
/// facetter är oåterkalleliga — de var det före #841 också.) (alla kolumner, inte
/// bara den som styrde filtret).
/// </para>
///
/// <para>
/// <b>Idempotens/restart:</b> NULL-kolumn-filtret gör körningen omstartbar —
/// en avbruten körning plockas upp av nästa enqueue. <c>OrderBy(ExternalId)</c>
/// ger deterministisk iterations-ordning. Race mot snapshot-cron är no-op-
/// overhead (UNIQUE-index + <see cref="JobAd.UpdateFromSource"/>-idempotens).
/// </para>
///
/// <para>
/// <b>Lager:</b> ren Application — ingen Hangfire-referens.
/// <c>[DisableConcurrentExecution]</c> bärs av respektive Worker-wrapper
/// (CLAUDE.md §2.1).
/// </para>
/// </summary>
public sealed partial class JobAdRefetchBackfillRunner(
    IJobSource jobSource,
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<JobAdRefetchBackfillRunner> logger)
{
    /// <summary>
    /// Kör en backfill-run. <paramref name="nullColumnPredicate"/> väljer raderna
    /// (typiskt <c>j =&gt; EF.Property&lt;string?&gt;(j, "&lt;ShadowColumn&gt;") == null</c>);
    /// källfiltret (<c>External.Source</c>) appliceras av kärnan.
    /// <paramref name="auditJobType"/> märker audit-raden (t.ex. "backfill").
    /// </summary>
    public async Task<BackfillCounts> RunAsync(
        Expression<Func<JobAd, bool>> nullColumnPredicate,
        BackfillRunnerOptions options,
        string auditJobType,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var source = jobSource.Source.Value;
        LogStarted(logger, source, auditJobType, options.PerItemDelayMs, options.MaxItemsPerRun);

        var counts = new BackfillCounts { StartedAt = startedAt };

        // Streamar via AsAsyncEnumerable — materialiserar ALDRIG hela listan i
        // minnet (ADR 0045 Beslut 3 Worker-mem soft cap 512 MiB). NULL-kolumn-
        // predikatet + källfiltret komponeras som två Where() (EF översätter till
        // AND). OrderBy(ExternalId) → deterministisk restart-ordning; ExternalId är
        // string (sorteras korrekt i Npgsql; JobAdId-VO saknar IComparable).
        var externalIdQuery = db.JobAds
            .Where(nullColumnPredicate)
            .Where(j => j.External != null && j.External.Source == jobSource.Source)
            .OrderBy(j => j.External!.ExternalId)
            .Select(j => j.External!.ExternalId)
            .AsNoTracking()
            .AsAsyncEnumerable();

        var perItemDelay = TimeSpan.FromMilliseconds(options.PerItemDelayMs);

        await foreach (var externalId in externalIdQuery.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Fetched++;

            if (counts.Fetched > options.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, options.MaxItemsPerRun);
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

                // Egen DI-scope per item (samma mönster som SyncPlatsbankenSnapshotJob).
                // Återställer ADR 0032 §5 single-command-scope-antagandet — change-
                // tracker lever och dör med ETT item.
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var mediator = itemScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new UpsertExternalJobAdCommand(jobSource.Source, externalId, refetched);
                var result = await mediator.Send(cmd, cancellationToken);

                if (result.IsFailure)
                {
                    counts.Errors++;
                    continue;
                }

                // Diskriminera UpsertOutcome — Added/Updated/Skipped (success-grenar).
                // Sammanslagning ljuger om backfill-progress (en archived annons →
                // handler returnerar Skipped, inte fel).
                switch (result.Value)
                {
                    case UpsertOutcome.Added: counts.Added++; break;
                    case UpsertOutcome.Updated: counts.Updated++; break;
                    case UpsertOutcome.Skipped: counts.SkippedByHandler++; break;
                }

                if (counts.RefetchAttempted % options.ProgressLogEvery == 0)
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

            // Sekventiell throttle mot JobTech. CancellationToken propageras → mid-
            // run abort via Hangfire-dashboard fungerar. ADR 0032-amendment-disciplin
            // (bounded ops mot JobTech) respekteras istället för Polly-queue-buildup.
            if (perItemDelay > TimeSpan.Zero)
                await Task.Delay(perItemDelay, cancellationToken);
        }

        var completedAt = clock.UtcNow;
        counts.CompletedAt = completedAt;

        LogCompleted(logger, source, auditJobType, counts.Fetched, counts.Updated,
            counts.NotFoundOnSource, counts.Errors, (completedAt - startedAt).TotalSeconds);

        // Audit-wire — återanvänder JobAdsSynced med JobType=auditJobType (inget
        // nytt audit-koncept). Skipped = handlerns Skipped + NotFoundOnSource
        // (källa-borta) — båda är "rad iterad men ej uppdaterad" ur retention-vy.
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: source,
            JobType: auditJobType,
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
        Message = "JobAdRefetchBackfill: startad — source={Source}, jobType={JobType}, perItemDelayMs={Delay}, maxItemsPerRun={Max}.")]
    private static partial void LogStarted(ILogger logger, string source, string jobType, int delay, int max);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Information,
        Message = "JobAdRefetchBackfill: progress — refetchAttempted={Attempted}, updated={Updated}, notFound={NotFound}, errors={Errors}.")]
    private static partial void LogProgress(ILogger logger, int attempted, int updated, int notFound, int errors);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Information,
        Message = "JobAdRefetchBackfill: klart — source={Source}, jobType={JobType}, fetched={Fetched}, updated={Updated}, notFound={NotFound}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, string source, string jobType, int fetched,
        int updated, int notFound, int errors, double durationSec);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Warning,
        Message = "JobAdRefetchBackfill: item-failure ExternalId={ExternalId} — räknas i Errors, fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, string externalId);

    [LoggerMessage(EventId = 6005, Level = LogLevel.Warning,
        Message = "JobAdRefetchBackfill: MaxItemsPerRun={MaxItems} nådd — bryter och avslutar gracefully. Idempotent: nästa körning plockar resterande NULL-rader.")]
    private static partial void LogMaxItemsReached(ILogger logger, int maxItems);
}

/// <summary>
/// Tunables för en backfill-run (throttle + cap). Frikopplad från IOptions-
/// bindningen så varje wrapper kan binda sin egen appsettings-sektion och mappa
/// hit (BackfillJobAdSsyk / BackfillJobAdKlass2).
/// </summary>
public sealed record BackfillRunnerOptions(int PerItemDelayMs, int MaxItemsPerRun, int ProgressLogEvery);

/// <summary>
/// Aggregerad statistik från en backfill-run. Loggas + returneras. Mutable by
/// design — runnern är ensam writer (per-iteration-counter-aggregering).
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
