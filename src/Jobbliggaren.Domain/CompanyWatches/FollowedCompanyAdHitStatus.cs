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
/// duplicating the three states is NOT a DRY violation (it avoids coupling two aggregates). This
/// enum has NO <c>Failed</c> member: the stranded-Queued reaper (TD-114) is a
/// <c>UserJobAdMatch</c>-specific observability concern, out of scope here — a send failure simply
/// strands the row in <see cref="Queued"/> (never re-sent), the same "never double-email &gt; never
/// miss" MVP trade-off the digest already makes.
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
}
