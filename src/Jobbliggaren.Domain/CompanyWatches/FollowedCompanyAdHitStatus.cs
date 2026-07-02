using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — the dedup state machine for a persisted
/// <see cref="FollowedCompanyAdHit"/>. A hit is born <see cref="Pending"/> (the
/// <c>CompanyWatchScanJob</c> persisted it but nothing was delivered yet), moves to
/// <see cref="Queued"/> when the dispatch step (the reused <c>DigestDispatchJob</c>) claims it, and
/// ends <see cref="Sent"/> when the follow-notification email was delivered. The transitions are
/// one-way and guarded in <see cref="FollowedCompanyAdHit"/> — re-running the scan never re-notifies
/// because an existing row in any non-<see cref="Pending"/> status is skipped (idempotency by
/// design; <c>UNIQUE(UserId, JobAdId, CompanyWatchId)</c> is the spine). Serialized by NAME
/// (reorder-safe).
/// <para>
/// <b>Deliberately a SEPARATE enum from <c>NotificationStatus</c> (ADR 0087 D5):</b> the shared
/// three-state SHAPE is a shared pattern, not shared knowledge — a company-follow hit models a
/// different concept ("a new ad appeared at an employer you follow") than a skill-graded match, so
/// duplicating the states is NOT a DRY violation (it avoids coupling two aggregates).
/// </para>
/// <para>
/// <b><see cref="Failed"/> — stranded-Queued recovery (#453 commit 2, external audit #26; supersedes
/// the original "no Failed member" trade-off):</b> the follow rail reuses the same claim-then-send
/// spine as the match rail (<c>Pending → MarkQueued</c> + commit → send → <c>MarkSent</c>), so a send
/// that never completes strands a row in <see cref="Queued"/> forever. The original ADR 0087 D5 posture
/// deliberately OMITTED a Failed state (deemed a <c>UserJobAdMatch</c>-specific concern), but that
/// re-shipped the exact permanent-invisible-strand failure class that <c>StrandedMatchReaperJob</c>
/// (TD-114) was raised to close on the match rail — and which ADR 0080's prod-Resend flip checklist
/// makes flip-blocking. So this rail now gets the same recovery: <see cref="Failed"/> is a terminal
/// state (never re-sent — honours "never double-email"), the reaper's follow arm moves a long-stranded
/// <see cref="Queued"/> row here so the strand becomes observable/queryable. Stored by NAME in the
/// existing <c>varchar(20)</c> column → NO migration (same trick the match rail used).
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FollowedCompanyAdHitStatus
{
    /// <summary>Persisted by the scan; no notification delivered yet.</summary>
    Pending,

    /// <summary>Claimed by the dispatch step; delivery in flight.</summary>
    Queued,

    /// <summary>The follow-notification (digest) email was delivered.</summary>
    Sent,

    /// <summary>
    /// #453 (audit #26) — terminal. The reaper moved this row here because it sat <see cref="Queued"/>
    /// past the strand threshold (a send that never completed). Never re-sent; makes a permanent strand
    /// observable instead of silently swallowed.
    /// </summary>
    Failed,
}
