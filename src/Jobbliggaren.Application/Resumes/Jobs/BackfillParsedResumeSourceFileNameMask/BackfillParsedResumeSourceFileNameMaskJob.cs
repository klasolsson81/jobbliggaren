using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask;

/// <summary>
/// #664 (#479 Low, GDPR Art. 5(1)(c)/25 minimisation) — the one-off backfill that re-masks any
/// personnummer left PLAINTEXT in <c>parsed_resumes.source_file_name</c> for rows imported BEFORE #465
/// (which added the masking at <see cref="ParsedResume.Create"/>). New imports already mask at the
/// factory seam; this closes the historical rows.
/// <para>
/// <b>EXECUTION IS KLAS-GATED (STOPP-5):</b> the update overwrites the plaintext personnummer in place
/// (irreversible — the point). Run with <c>dryRun: true</c> FIRST and put the count report in front of
/// Klas before the destructive run; never run destructively unprompted.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>DEK-FREE set-based, NEVER load-materialise (senior-cto-advisor 2026-06-25, the governing
/// ParsedResumeRetentionJob rule).</b> <see cref="ParsedResume"/> carries CV-PII encrypted ON ITSELF
/// (<c>raw_text</c> Form A + <c>parsed_content_enc</c> Form B), so materialising it engages the
/// field-decryption interceptor and pulls encrypted CV-PII into memory the mask never needs
/// (PII-minimisation, §5). Instead this streams a plaintext-only PROJECTION (id + filename + deletedAt
/// — no entity materialised, no interceptor) and applies the change via
/// <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync"/> on the plaintext column only.
/// This is the M1 mechanism that supersedes the STEG-4 A2 "through the aggregate" bind (A2 mirrored
/// #544's <c>CompanyWatch</c>, which carries NO encrypted field).
/// </para>
/// <para>
/// <b>The redaction rule is C#, never SQL:</b> <see cref="PersonnummerRedactor"/> (Luhn+date-gated,
/// gap-aware, length-preserving) is the SAME recogniser <see cref="ParsedResume.Create"/> applies — the
/// SSOT. A <c>substring</c>-in-DB predicate would be a second recogniser (#844/#544: "a rule with two
/// normalisers is two rules"), so the scan filters in memory against the one recogniser.
/// </para>
/// <para>
/// <b>Idempotent by shape:</b> <see cref="PersonnummerRedactor.Redact"/> is a no-op unless the filename
/// holds a REAL personnummer (a year/phone-run is untouched; an already-masked <c>*</c>-run is not
/// personnummer-shaped). So a re-run, or the run after a crash, never re-touches a row.
/// </para>
/// <para>
/// <b>Covers soft-deleted rows (B5):</b> the projection uses <c>IgnoreQueryFilters</c> — a promoted or
/// discarded (soft-deleted) row still holds the plaintext filename and MUST be masked too, else the
/// "strict improvement" claim is false for that subset.
/// </para>
/// <para>
/// <b>Two-phase (Postgres has no MARS):</b> the projection reader is fully drained BEFORE any
/// <c>ExecuteUpdate</c> — issuing a command mid-stream on the same connection would fail. Only the tiny
/// CHANGED set is held between phases (most rows have no personnummer → skipped, never buffered).
/// </para>
/// <para>
/// <b>No plaintext in a log, ever (§5):</b> LoggerMessages carry counts + opaque ids only — NEVER the filename (it
/// may itself contain the personnummer); the per-item failure path logs only an opaque ParsedResumeId (a surrogate GUID, a pseudonym not PII, parity BackfillCompanyWatchOrgNrTokenJob) and the already-masked value, never the plaintext. <b>Layer:</b> pure Application, no Hangfire reference
/// (the admin endpoint enqueues this class directly, parity <c>BackfillCompanyWatchOrgNrTokenJob</c>).
/// One-off; never registered as recurring.
/// </para>
/// </remarks>
public sealed partial class BackfillParsedResumeSourceFileNameMaskJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    IOptions<BackfillParsedResumeSourceFileNameMaskOptions> options,
    ILogger<BackfillParsedResumeSourceFileNameMaskJob> logger)
{
    public async Task<ParsedResumeSourceFileNameMaskBackfillCounts> RunAsync(
        bool dryRun, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var startedAt = clock.UtcNow;
        LogStarted(logger, dryRun, o.MaxItemsPerRun);

        var counts = new ParsedResumeSourceFileNameMaskBackfillCounts { DryRun = dryRun, StartedAt = startedAt };

        // Phase 1 — scan a DEK-FREE projection (plaintext columns only → no entity materialised, no
        // field-decryption interceptor). IgnoreQueryFilters reaches soft-deleted promoted/discarded rows
        // that still hold the plaintext filename (B5). Deterministic order; never materialize the aggregate.
        var projection = db.ParsedResumes
            .IgnoreQueryFilters()
            .OrderBy(p => p.Id)
            .Select(p => new { Id = p.Id.Value, p.SourceFileName, p.DeletedAt })
            .AsAsyncEnumerable();

        // Only CHANGED rows are buffered (tiny — most filenames carry no personnummer).
        var toMask = new List<(Guid Id, string Redacted, bool SoftDeleted)>();

        await foreach (var row in projection.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts.Seen++;

            if (counts.Seen > o.MaxItemsPerRun)
            {
                LogMaxItemsReached(logger, o.MaxItemsPerRun);
                counts.Seen--;
                break;
            }

            // The one recogniser (SSOT), in memory — same masker ParsedResume.Create applies.
            var redacted = PersonnummerRedactor.Redact(row.SourceFileName);
            if (string.Equals(redacted, row.SourceFileName, StringComparison.Ordinal))
            {
                // No real personnummer, or already masked (idempotent-by-shape).
                counts.Skipped++;
                continue;
            }

            toMask.Add((row.Id, redacted, row.DeletedAt.HasValue));

            if ((counts.Skipped + toMask.Count) % o.ProgressLogEvery == 0)
                LogProgress(logger, toMask.Count, counts.Seen);
        }

        if (dryRun)
        {
            // Report the candidate set (what a live run WOULD mask). No writes.
            counts.Masked = toMask.Count;
            counts.SoftDeletedMasked = toMask.Count(t => t.SoftDeleted);
            return Complete(counts, startedAt, dryRun);
        }

        // Phase 2 — apply, AFTER the projection reader is drained. Each ExecuteUpdate writes ONLY the
        // plaintext source_file_name column (SetProperty targets the mapped column, not the private CLR
        // setter — parity ExecuteDelete on this aggregate). Redact is length-preserving, so the
        // ≤400/non-empty construction invariants are preserved and the masking invariant is exactly what
        // the update establishes. Per-owner isolation is moot: no DEK is touched.
        var perItemDelay = TimeSpan.FromMilliseconds(o.PerItemDelayMs);
        foreach (var (id, redacted, softDeleted) in toMask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsedResumeId = new ParsedResumeId(id);
            try
            {
                await db.ParsedResumes
                    .IgnoreQueryFilters()
                    .Where(p => p.Id == parsedResumeId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(p => p.SourceFileName, redacted), cancellationToken);

                counts.Masked++;
                if (softDeleted)
                    counts.SoftDeletedMasked++;

                if (counts.Masked % o.ProgressLogEvery == 0)
                    LogProgress(logger, counts.Masked, counts.Seen);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.Errors++;
                LogItemFailed(logger, ex, id);
            }

            if (perItemDelay > TimeSpan.Zero)
                await Task.Delay(perItemDelay, cancellationToken);
        }

        return Complete(counts, startedAt, dryRun);
    }

    private ParsedResumeSourceFileNameMaskBackfillCounts Complete(
        ParsedResumeSourceFileNameMaskBackfillCounts counts, DateTimeOffset startedAt, bool dryRun)
    {
        var completedAt = clock.UtcNow;
        counts.CompletedAt = completedAt;
        LogCompleted(logger, dryRun, counts.Seen, counts.Masked, counts.SoftDeletedMasked,
            counts.Skipped, counts.Errors, (completedAt - startedAt).TotalSeconds);
        return counts;
    }

    [LoggerMessage(EventId = 6641, Level = LogLevel.Information,
        Message = "BackfillParsedResumeSourceFileNameMask: startad — dryRun={DryRun}, maxItemsPerRun={Max}. "
            + "DEK-fri projektion + ExecuteUpdate, ingen aggregat-materialisering. Counts only — aldrig ett filnamn i loggen.")]
    private static partial void LogStarted(ILogger logger, bool dryRun, int max);

    [LoggerMessage(EventId = 6642, Level = LogLevel.Information,
        Message = "BackfillParsedResumeSourceFileNameMask: progress — masked={Masked}, seen={Seen}.")]
    private static partial void LogProgress(ILogger logger, int masked, int seen);

    [LoggerMessage(EventId = 6643, Level = LogLevel.Warning,
        Message = "BackfillParsedResumeSourceFileNameMask: maxItemsPerRun={Max} nått — avbryter skanningen.")]
    private static partial void LogMaxItemsReached(ILogger logger, int max);

    [LoggerMessage(EventId = 6644, Level = LogLevel.Warning,
        Message = "BackfillParsedResumeSourceFileNameMask: rad {ParsedResumeId} misslyckades — fortsätter.")]
    private static partial void LogItemFailed(ILogger logger, Exception exception, Guid parsedResumeId);

    [LoggerMessage(EventId = 6645, Level = LogLevel.Warning,
        Message = "BackfillParsedResumeSourceFileNameMask: klar — dryRun={DryRun}, seen={Seen}, masked={Masked}, "
            + "softDeletedMasked={SoftDeletedMasked}, skipped={Skipped}, errors={Errors}, {Seconds}s. "
            + "STOPP-5: plaintext-personnummer skrivs över irreversibelt på maskerade rader; Klas ser dry-run-deltat FÖRE accept.")]
    private static partial void LogCompleted(ILogger logger, bool dryRun, int seen, int masked,
        int softDeletedMasked, int skipped, int errors, double seconds);
}

/// <summary>
/// The STOPP-5 delta report, in numbers a human reviews. On a dry run <see cref="Masked"/> = the rows
/// that WOULD be masked; on a live run = the rows actually masked. <see cref="SoftDeletedMasked"/> = of
/// those, the soft-deleted (promoted/discarded) rows (the B5 coverage witness). <see cref="Skipped"/> =
/// rows with no real personnummer or already masked (idempotent-by-shape).
/// </summary>
public sealed class ParsedResumeSourceFileNameMaskBackfillCounts
{
    public bool DryRun { get; init; }
    public int Seen { get; set; }
    public int Masked { get; set; }
    public int SoftDeletedMasked { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}
