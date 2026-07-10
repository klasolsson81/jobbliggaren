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
/// it engages the field-decryption materialization interceptor — in an authenticated-owner scope
/// without a warmed DEK that throws, and in this system (no-owner, cross-user) Worker scope it
/// passes through leaving the ciphertext unread; either way load-then-Remove would pull encrypted
/// CV-PII into Worker memory the delete never needs (PII-minimisation, §5). A pure DELETE has no
/// field to decrypt, so <c>ExecuteDeleteAsync</c> is the correct DEK-free mechanism (parity
/// <c>AccountHardDeleter</c>'s set-based crypto-erasure delete). Three per-status calls give a
/// PII-free per-arm count for the log. <c>IgnoreQueryFilters()</c> reaches the soft-deleted
/// Discarded/Promoted rows (the default <c>DeletedAt == null</c> filter hides them).
/// </para>
/// <para>
/// <b>Original-file coupling (Fas 4b PR-9a, ADR 0100 / DPIA M-F3):</b> the Discarded and
/// abandoned-PendingReview arms first sweep the <c>resume_files</c> originals whose
/// <c>parsed_resume_id</c> anchors to a matured row (same DEK-free set-delete; files before
/// parsed so the anchor survives a partial failure). The Promoted arm never sweeps files —
/// a promoted original graduates to the canonical Resume's lifetime.
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
        // Separate per-class cutoffs (even where the window is currently equal) so the arms stay
        // correct if a window ever diverges — no silent coupling.
        var discardedCutoff = now.AddDays(-DiscardedRetentionDays);
        var promotedCutoff = now.AddDays(-PromotedRetentionDays);
        var pendingCutoff = now.AddDays(-AbandonedPendingReviewRetentionDays);

        // Fas 4b PR-9a (ADR 0100, DPIA M-F3) — the original-file rows (resume_files) follow the
        // STAGING-DEATH arms of the parsed lifecycle: an original never outlives its Discarded/
        // abandoned parsed sibling. The file delete runs FIRST in each coupled arm (the parsed row
        // is the predicate anchor — after the parsed delete the sibling would be unfindable and the
        // original would orphan) with the SAME DEK-free set-based mechanism (a pure DELETE never
        // materialises the multi-MB Form C bytea). The OUTER IgnoreQueryFilters disables the
        // ParsedResume soft-delete filter inside the correlated subquery too (EF query-wide
        // semantics) — required to see the soft-deleted Discarded rows. Crash-ordering is safe:
        // files-then-parsed means a failure between the two leaves the parsed anchor in place and
        // the next run re-sweeps (idempotent); the invariant "original never outlives parsed"
        // cannot be violated by partial failure.
        //
        // The PROMOTED arm is deliberately NOT coupled: a promoted original graduates to the
        // canonical Resume's lifetime (DPIA M-F3 "promoted originals live with the Resume" —
        // P1 "filen är helig"); only the redundant parsed STAGING row is swept below. A promoted
        // original's erasure paths are the Art. 17 cascade (AccountHardDeleter, owner-scoped) and
        // the coming resume-lifecycle coupling (PR-9b-era follow-up, ADR 0100).
        var discardedFiles = await db.ResumeFiles
            .IgnoreQueryFilters()
            .Where(f => db.ParsedResumes.Any(p => p.Id == f.ParsedResumeId
                && p.Status == ParsedResumeStatus.Discarded
                && p.DeletedAt < discardedCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        // Discarded — soft-deleted (DeletedAt set) → IgnoreQueryFilters; aged by DeletedAt.
        var discarded = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.Discarded && p.DeletedAt < discardedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Promoted — soft-deleted at promotion; the canonical Resume holds the data, staging is
        // redundant. The original file is NOT swept here (graduation — see the M-F3 note above).
        var promotedExpired = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.Promoted && p.DeletedAt < promotedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Abandoned-arm file coupling (M-F3): the 90d-abandoned upload's original goes with it.
        var abandonedFiles = await db.ResumeFiles
            .IgnoreQueryFilters()
            .Where(f => db.ParsedResumes.Any(p => p.Id == f.ParsedResumeId
                && p.Status == ParsedResumeStatus.PendingReview
                && p.DeletedAt == null
                && p.CreatedAt < pendingCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        // Abandoned PendingReview — never soft-deleted (DeletedAt null); aged by CreatedAt.
        var pendingReviewExpired = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.Status == ParsedResumeStatus.PendingReview
                        && p.DeletedAt == null
                        && p.CreatedAt < pendingCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // discardedCutoff == promotedCutoff today (both 30d) → one "DeletedAt <" cutoff in the log.
        LogComplete(
            logger, discarded, promotedExpired, pendingReviewExpired,
            discardedFiles + abandonedFiles, discardedCutoff, pendingCutoff);
    }

    // PII-free: integer counts + cutoffs only — never an id / job-seeker-id / file name (§5).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "ParsedResumeRetentionJob: prunade {Discarded} Discarded + {PromotedExpired} Promoted (DeletedAt < {DeletedCutoff:yyyy-MM-dd}) + {PendingReviewExpired} övergivna PendingReview (CreatedAt < {PendingCutoff:yyyy-MM-dd}) + {CoupledOriginalFiles} kopplade originalfiler (M-F3; Promoted-original sveps aldrig här)")]
    private static partial void LogComplete(
        ILogger logger, int discarded, int promotedExpired, int pendingReviewExpired,
        int coupledOriginalFiles, DateTimeOffset deletedCutoff, DateTimeOffset pendingCutoff);
}
