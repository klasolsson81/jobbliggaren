using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub;

/// <summary>
/// #842 Tier A (ADR 0106, re-bind R7/D10) — the one-off backfill that applies the recruiter
/// contact scrub to the ~93k ads imported BEFORE the aggregate invariant landed.
/// <b>EXECUTION IS KLAS-GATED (STOPP-5)</b>: the scrub re-derives <c>extracted_terms</c>, so match
/// grades shift corpus-wide — a correction (the removed tokens were PII noise, never legitimate
/// signal), but user-visible. Run with <c>dryRun: true</c> FIRST and put the report in front of
/// Klas; never run destructively unprompted.
/// </summary>
/// <remarks>
/// <para>
/// <b>R7's two passes, collapsed into one LOCAL sweep — and where the second half went.</b> The
/// re-bind's Pass 1 (still-listed ads: populate declared contacts + scrub, from a fresh JobTech
/// fetch) is served by the very next nightly snapshot sync: once the invariant is on main, every
/// listed ad is re-ingested through <c>UpdateFromSource</c> within 24 h, which populates
/// <c>contacts</c> from the source's <c>application_contacts</c> AND re-scrubs the body — a
/// duplicate fetch here would add a rate-limit window and a second ingest path for nothing. What
/// the sync can NEVER reach is Pass 2, the DE-LISTED tail: an ad that left the feed is never
/// rewritten, so its stored body keeps the address forever (the forward-only hole D10 exists to
/// close). This job is that pass — a local re-projection over the STORED text, no JobTech call
/// (pinned the same way as the extraction backfill) — run over every non-Erased imported ad, so
/// still-listed ads are also scrubbed immediately rather than "within 24 h" (the delta report
/// then measures the whole corpus in one controlled run instead of dribbling through the sync).
/// </para>
/// <para>
/// <b>Term preservation:</b> re-extraction after the scrub uses the 2-arg
/// <c>JobAdExtractionInput</c> (no requirement source locally — same constraint as the extraction
/// backfill), so the ad's existing <c>Requirement</c>-kind terms are CARRIED OVER explicitly:
/// they derive from the employer's structured must_have/nice_to_have skills (taxonomy concept
/// ids + labels, never free text), cannot carry a contact, and dropping them would degrade
/// matching for every de-listed ad permanently.
/// </para>
/// <para>
/// <b>Idempotent by the probe:</b> <c>JobAd.ApplyContactScrubBackfill</c> mutates nothing when
/// the detector finds nothing — scrubbed text is a fixed point, so a re-run (or the run after a
/// crash) touches only rows that still carry a detection. There is deliberately NO SQL predicate:
/// no query can run the detector, and a proxy predicate (<c>description LIKE '%@%'</c>) is a
/// second, weaker recogniser (#844 — a rule with two normalisers is two rules).
/// </para>
/// <para>
/// <b>Layer:</b> pure Application — no Hangfire reference (the admin endpoint enqueues this class
/// directly, parity backfill-extraction). One-off; never registered as recurring.
/// </para>
/// </remarks>
public sealed partial class BackfillRecruiterContactScrubJob(
    IServiceScopeFactory scopeFactory,
    IAppDbContext db,
    IJobAdKeywordExtractor extractor,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    IOptions<BackfillRecruiterContactScrubOptions> options,
    ILogger<BackfillRecruiterContactScrubJob> logger)
{
    public async Task<ContactScrubBackfillCounts> RunAsync(
        bool dryRun, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        LogStarted(logger, dryRun, o.MaxItemsPerRun);

        var counts = new ContactScrubBackfillCounts { DryRun = dryRun, StartedAt = startedAt };

        // Stream ids of every IMPORTED, non-Erased ad (manual ads are never scrubbed — §5; the
        // tombstone holds nothing). Deterministic order; never materialize 93k rows (ADR 0045).
        var idQuery = db.JobAds
            .Where(j => j.External != null && j.Status != JobAdStatus.Erased)
            .OrderBy(j => j.Id)
            .Select(j => j.Id.Value)
            .AsAsyncEnumerable();

        var perItemDelay = TimeSpan.FromMilliseconds(o.PerItemDelayMs);

        await foreach (var guid in idQuery.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Seen++;

            if (counts.Seen > o.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, o.MaxItemsPerRun);
                counts.Seen--;
                break;
            }

            var id = new JobAdId(guid);
            try
            {
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var scopedDb = itemScope.ServiceProvider.GetRequiredService<IAppDbContext>();

                var jobAd = await scopedDb.JobAds
                    .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

                if (jobAd is null)
                {
                    counts.Skipped++;
                    continue;
                }

                var scrub = jobAd.ApplyContactScrubBackfill();
                if (scrub.IsFailure || !scrub.Value)
                {
                    counts.Skipped++;
                    continue;
                }

                // The scrub changed the text the terms derive from → re-extract from the
                // aggregate's post-scrub fields, carrying the Requirement-kind terms over (see
                // the class remarks — they are taxonomy data, unreachable by the detector and
                // un-rederivable locally).
                var requirementTerms = jobAd.ExtractedTerms?.Terms
                    .Where(t => t.Kind == ExtractedTermKind.Requirement) ?? [];
                var reExtracted = extractor.Extract(
                    new JobAdExtractionInput(jobAd.Title, jobAd.Description));
                jobAd.SetExtractedTerms(
                    ExtractedTerms.From([.. reExtracted.Terms, .. requirementTerms]));

                counts.Scrubbed++;
                if (jobAd.Contacts is { IsEmpty: false })
                    counts.ContactsPromoted += jobAd.Contacts.Contacts.Count;

                if (!dryRun)
                    await scopedDb.SaveChangesAsync(cancellationToken);

                if (counts.Scrubbed % o.ProgressLogEvery == 0)
                    LogProgress(logger, counts.Scrubbed, counts.Seen, counts.Errors);
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
        LogCompleted(logger, dryRun, counts.Seen, counts.Scrubbed, counts.ContactsPromoted,
            counts.Skipped, counts.Errors, (completedAt - startedAt).TotalSeconds);

        // Reuse JobAdsSynced (no new audit concept — parity backfill-extraction). The DRY RUN is
        // audited too: the STOPP-5 report Klas reviews must itself be accountable.
        await auditor.RecordAsync(new JobAdsSynced(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: JobSource.Platsbanken.Value,
            JobType: dryRun ? "backfill-contact-scrub-dryrun" : "backfill-contact-scrub",
            Fetched: counts.Seen,
            Added: 0,
            Updated: dryRun ? 0 : counts.Scrubbed,
            Archived: 0,
            Skipped: counts.Skipped,
            Errors: counts.Errors,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken);

        return counts;
    }

    [LoggerMessage(EventId = 6151, Level = LogLevel.Information,
        Message = "BackfillRecruiterContactScrub: startad — dryRun={DryRun}, maxItemsPerRun={Max}. "
            + "Lokal re-projektion, ingen JobTech-fetch. Counts only — aldrig en kontaktuppgift i loggen.")]
    private static partial void LogStarted(ILogger logger, bool dryRun, int max);

    [LoggerMessage(EventId = 6152, Level = LogLevel.Information,
        Message = "BackfillRecruiterContactScrub: progress — scrubbed={Scrubbed}, seen={Seen}, errors={Errors}.")]
    private static partial void LogProgress(ILogger logger, int scrubbed, int seen, int errors);

    [LoggerMessage(EventId = 6153, Level = LogLevel.Warning,
        Message = "BackfillRecruiterContactScrub: maxItemsPerRun={Max} nått — avbryter; re-enqueue fortsätter (idempotent).")]
    private static partial void LogMaxItemsReached(ILogger logger, int max);

    [LoggerMessage(EventId = 6154, Level = LogLevel.Warning,
        Message = "BackfillRecruiterContactScrub: item {JobAdId} misslyckades — fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, Guid jobAdId);

    [LoggerMessage(EventId = 6155, Level = LogLevel.Warning,
        Message = "BackfillRecruiterContactScrub: klar — dryRun={DryRun}, seen={Seen}, scrubbed={Scrubbed}, "
            + "contactsPromoted={ContactsPromoted}, skipped={Skipped}, errors={Errors}, {Seconds}s. "
            + "STOPP-5: extracted_terms har ändrats på scrubbade rader — matchgrader skiftar; Klas ser deltat FÖRE accept.")]
    private static partial void LogCompleted(ILogger logger, bool dryRun, int seen, int scrubbed,
        int contactsPromoted, int skipped, int errors, double seconds);
}

/// <summary>
/// The STOPP-5 delta report, in numbers a human reviews. <c>Scrubbed</c> = ads whose text carried
/// at least one detection (on a dry run: WOULD have been scrubbed); <c>ContactsPromoted</c> = the
/// contact entries now held structurally instead of in free text.
/// </summary>
public sealed class ContactScrubBackfillCounts
{
    public bool DryRun { get; init; }
    public int Seen { get; set; }
    public int Scrubbed { get; set; }
    public int ContactsPromoted { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}
