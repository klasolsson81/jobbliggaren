using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention;

/// <summary>
/// TD-111 (ADR 0074 F4-8) — scheduled retention sweep for <see cref="ParsedResume"/> staging
/// artifacts (GDPR Art. 5(1)(e) storage-limitation). F4-8 persists a parsed CV DEK-encrypted at
/// rest until the user acts; without a sweep a Discarded row, a promoted-and-superseded staging
/// row, or a never-promoted (abandoned) PendingReview row would sit forever. This hard-deletes
/// matured rows. Three classes, three windows (Klas 2026-06-25):
/// <list type="bullet">
/// <item>Discarded ≥ 30 d — aged by <c>DeletedAt</c> (Discard soft-deletes);</item>
/// <item>Promoted ≥ 30 d — aged by <c>DeletedAt</c> (Promote soft-deletes; the canonical
///   <c>Resume</c> already holds the data, the staging copy is redundant);</item>
/// <item>abandoned PendingReview ≥ 90 d — aged by <c>CreatedAt</c> (never soft-deleted →
///   <c>DeletedAt == null</c>; a generous return-window for the user to finish the import).</item>
/// </list>
/// <para>
/// <b>Mechanism (senior-cto-advisor 2026-06-25): set-based <see cref="EntityFrameworkQueryableExtensions"/>
/// <c>ExecuteDeleteAsync</c>, DEK-FREE.</b> The <see cref="ParsedResume"/> aggregate carries CV-PII
/// encrypted ON ITSELF (<c>raw_text</c> Form A + <c>parsed_content_enc</c> Form B), so MATERIALISING
/// it fires the field-decryption materialization interceptor → throws in this DEK-free cross-user
/// Worker scope (and would pull PII into memory the delete never reads — PII-minimisation, §5). A
/// pure DELETE has no field to decrypt, so <c>ExecuteDeleteAsync</c> is the correct DEK-free
/// mechanism (parity <c>AccountHardDeleter</c>'s set-based crypto-erasure delete). Three per-status
/// calls give a PII-free per-arm count for the log. <c>IgnoreQueryFilters()</c> reaches the
/// soft-deleted Discarded/Promoted rows (the default <c>DeletedAt == null</c> filter hides them).
/// </para>
/// <para>
/// <b>Idempotency is the concurrency-safety contract</b> (no xmin guard — a delete-by-predicate has
/// no read-modify-write window; a row missed on a failed run is re-swept next run). <b>No per-user
/// audit row</b> — a system retention job has no actor (mirrors <c>AccountHardDeleter</c>, which
/// anonymises rather than appends); writing per-user audit on already-soft-deleted artifacts would
/// be PII-bearing noise. The per-run log carries ONLY integer counts + cutoffs (PII-free, §5 —
/// never a ParsedResumeId / JobSeekerId / file name). Registered nightly. NO AI/LLM (ADR 0071).
/// </para>
/// </summary>
public sealed partial class ParsedResumeRetentionJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<ParsedResumeRetentionJob> logger)
{
    /// <summary>Discarded staging rows: retained this many days after Discard (parity the
    /// account-restore window). Hardcoded this phase — flip to IOptions if policy changes.</summary>
    private const int DiscardedRetentionDays = 30;

    /// <summary>Promoted staging rows: retained this many days after promotion (the canonical
    /// Resume already holds the data — short retention).</summary>
    private const int PromotedRetentionDays = 30;

    /// <summary>Abandoned PendingReview rows (never promoted/discarded): a generous window for the
    /// user to return and finish before the upload is purged.</summary>
    private const int AbandonedPendingReviewRetentionDays = 90;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var deletedCutoff = now.AddDays(-DiscardedRetentionDays);            // == PromotedRetentionDays
        var pendingCutoff = now.AddDays(-AbandonedPendingReviewRetentionDays);

        // Discarded — soft-deleted (DeletedAt set) → IgnoreQueryFilters; aged by DeletedAt.
        var discarded = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.Discarded && p.DeletedAt < deletedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Promoted — soft-deleted at promotion; the canonical Resume holds the data, staging is redundant.
        var promotedExpired = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.Promoted && p.DeletedAt < deletedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Abandoned PendingReview — never soft-deleted (DeletedAt null); aged by CreatedAt.
        var pendingReviewExpired = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.PendingReview
                        && p.DeletedAt == null
                        && p.CreatedAt < pendingCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        LogComplete(logger, discarded, promotedExpired, pendingReviewExpired, deletedCutoff, pendingCutoff);
    }

    // PII-free: integer counts + cutoffs only — never an id / job-seeker-id / file name (§5).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "ParsedResumeRetentionJob: prunade {Discarded} Discarded + {PromotedExpired} Promoted (DeletedAt < {DeletedCutoff:yyyy-MM-dd}) + {PendingReviewExpired} övergivna PendingReview (CreatedAt < {PendingCutoff:yyyy-MM-dd})")]
    private static partial void LogComplete(
        ILogger logger, int discarded, int promotedExpired, int pendingReviewExpired,
        DateTimeOffset deletedCutoff, DateTimeOffset pendingCutoff);
}
