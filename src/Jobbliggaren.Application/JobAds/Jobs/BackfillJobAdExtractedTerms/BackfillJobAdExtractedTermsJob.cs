using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — one-off LOCAL backfill of the
/// deterministic keyword/skill extraction (<c>extracted_terms</c>) for the ~54k
/// job ads imported before F4-4.
/// <para>
/// <b>Deliberately NOT a wrapper over <see cref="Common.JobAdRefetchBackfillRunner"/></b>
/// (dotnet-architect decision 5): that runner re-fetches each ad from JobTech
/// because its target columns are STORED-generated from a re-written
/// <c>raw_payload</c>. F4-4 needs NO re-fetch — title + description are already
/// stored on every row, so the extraction is a pure <b>local re-projection</b>:
/// stream the un-extracted rows, run the in-process
/// <see cref="IJobAdKeywordExtractor"/>, persist. No JobTech GET, no rate-limit
/// window, no network dependency. (A test pins that <c>IJobSource</c> is never
/// called.)
/// </para>
/// <para>
/// <b>Predicate:</b> the STORED-generated <c>extracted_lexemes</c> shadow column
/// <c>IS NULL</c> — true iff <c>extracted_terms IS NULL</c> (never extracted), and
/// false once written (even an empty <c>'[]'</c> extraction → <c>'{}'</c>). This is
/// what makes the run idempotent/restart-safe: a re-enqueue picks up only the
/// remaining NULL rows.
/// </para>
/// <para>
/// <b>Layer:</b> pure Application — no Hangfire reference;
/// <c>[DisableConcurrentExecution]</c> is carried by the Worker wrapper
/// (CLAUDE.md §2.1). One-off operation, enqueued fire-and-forget; never registered
/// as a recurring job.
/// </para>
/// </summary>
public sealed partial class BackfillJobAdExtractedTermsJob(
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IJobAdKeywordExtractor extractor,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    IOptions<BackfillJobAdExtractedTermsOptions> options,
    ILogger<BackfillJobAdExtractedTermsJob> logger)
{
    public async Task<BackfillExtractionCounts> RunAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        return await RunAsync(
            new BackfillExtractionRunOptions(o.PerItemDelayMs, o.MaxItemsPerRun, o.ProgressLogEvery),
            cancellationToken);
    }

    public async Task<BackfillExtractionCounts> RunAsync(
        BackfillExtractionRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        LogStarted(logger, options.MaxItemsPerRun);

        var counts = new BackfillExtractionCounts { StartedAt = startedAt };

        // Stream ids of NOT-YET-extracted rows via AsAsyncEnumerable — never
        // materialize 54k in memory (ADR 0045 Beslut 3, Worker mem soft cap). The
        // STORED-generated extracted_lexemes shadow IS NULL ⟺ extracted_terms IS
        // NULL → idempotent restart; OrderBy(Id) gives a deterministic order.
        var idQuery = db.JobAds
            // extracted_lexemes IS NULL ⟺ extracted_terms IS NULL (the STORED
            // generated jsonb shadow). NULL = never extracted; '[]'/populated =
            // extracted → idempotent restart.
            .Where(j => EF.Property<string?>(j, "ExtractedLexemes") == null)
            .OrderBy(j => j.Id)
            .Select(j => j.Id.Value) // project the Guid — deterministic stream, no tracking
            .AsAsyncEnumerable();

        var perItemDelay = TimeSpan.FromMilliseconds(options.PerItemDelayMs);

        await foreach (var guid in idQuery.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Seen++;

            if (counts.Seen > options.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, options.MaxItemsPerRun);
                counts.Seen--; // not processed
                break;
            }

            var id = new JobAdId(guid);
            try
            {
                // Own DI scope per item — a fresh tracking AppDbContext that lives
                // and dies with ONE ad (parity the refetch runner / snapshot job).
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var scopedDb = itemScope.ServiceProvider.GetRequiredService<IAppDbContext>();

                var jobAd = await scopedDb.JobAds
                    .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

                if (jobAd is null)
                {
                    // Raced away (archived/purged) between the id-stream and the load.
                    counts.Skipped++;
                    continue;
                }

                // INTENTIONALLY no structured requirements (the 2-arg JobAdExtractionInput):
                // this is a pure LOCAL re-projection over the already-stored title+description
                // and has no requirement source — must_have/nice_to_have skills come only from
                // a JobTech response (PlatsbankenJobSource.MapRequirements), which this job
                // deliberately never re-fetches. The requirement backfill is a SEPARATE job
                // (BackfillJobAdRequirementsJob, F4-4b) that refetches the full payload WITH
                // requirements and subsumes this one. The idempotent predicate (extracted_lexemes
                // IS NULL) means this call only ever runs on never-extracted rows and can never
                // strip an ad that already carries requirement terms (#268 audit, F3).
                var terms = extractor.Extract(new JobAdExtractionInput(jobAd.Title, jobAd.Description));
                jobAd.SetExtractedTerms(terms);
                await scopedDb.SaveChangesAsync(cancellationToken);

                counts.Extracted++;
                if (counts.Extracted % options.ProgressLogEvery == 0)
                    LogProgress(logger, counts.Extracted, counts.Skipped, counts.Errors);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.Errors++;
                LogItemFailed(logger, ex, id.Value);
            }

            if (perItemDelay > TimeSpan.Zero)
                await Task.Delay(perItemDelay, cancellationToken);
        }

        var completedAt = clock.UtcNow;
        counts.CompletedAt = completedAt;
        LogCompleted(logger, counts.Seen, counts.Extracted, counts.Skipped, counts.Errors,
            (completedAt - startedAt).TotalSeconds);

        // Reuse JobAdsSynced (no new audit concept) with JobType=backfill-extraction.
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: JobSource.Platsbanken.Value,
            JobType: "backfill-extraction",
            Fetched: counts.Seen,
            Added: 0,
            Updated: counts.Extracted,
            Archived: 0,
            Skipped: counts.Skipped,
            Errors: counts.Errors,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken);

        return counts;
    }

    [LoggerMessage(EventId = 6101, Level = LogLevel.Information,
        Message = "BackfillJobAdExtractedTerms: startad — maxItemsPerRun={Max}. Lokal re-projektion, ingen JobTech-fetch.")]
    private static partial void LogStarted(ILogger logger, int max);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Information,
        Message = "BackfillJobAdExtractedTerms: progress — extracted={Extracted}, skipped={Skipped}, errors={Errors}.")]
    private static partial void LogProgress(ILogger logger, int extracted, int skipped, int errors);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Information,
        Message = "BackfillJobAdExtractedTerms: klart — seen={Seen}, extracted={Extracted}, skipped={Skipped}, errors={Errors}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, int seen, int extracted, int skipped, int errors, double durationSec);

    [LoggerMessage(EventId = 6104, Level = LogLevel.Warning,
        Message = "BackfillJobAdExtractedTerms: item-failure JobAdId={JobAdId} — räknas i Errors, fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, Guid jobAdId);

    [LoggerMessage(EventId = 6105, Level = LogLevel.Warning,
        Message = "BackfillJobAdExtractedTerms: MaxItemsPerRun={MaxItems} nådd — bryter gracefully. Idempotent: nästa körning tar resterande NULL-rader.")]
    private static partial void LogMaxItemsReached(ILogger logger, int maxItems);
}

/// <summary>Tunables for one extraction-backfill run (no external throttle needed
/// — local re-projection). Decoupled from the IOptions binding so the run can be
/// driven with explicit values in tests.</summary>
public sealed record BackfillExtractionRunOptions(int PerItemDelayMs, int MaxItemsPerRun, int ProgressLogEvery);

/// <summary>Aggregated statistics from an extraction-backfill run. Mutable by
/// design — the job is the sole writer (per-iteration counters).</summary>
public sealed class BackfillExtractionCounts
{
    public int Seen { get; set; }       // un-extracted rows iterated (= input)
    public int Extracted { get; set; }  // rows extracted + persisted
    public int Skipped { get; set; }    // raced away between id-stream and load
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
