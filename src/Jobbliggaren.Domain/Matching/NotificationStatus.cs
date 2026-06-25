using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Matching;

/// <summary>
/// ADR 0080 Vag 4 — the dedup state machine for a persisted <see cref="UserJobAdMatch"/>.
/// A match is born <see cref="Pending"/> (the Worker scan persisted it but nothing was
/// delivered yet), moves to <see cref="Queued"/> when the dispatch step picks it up, and
/// ends <see cref="Sent"/> when the notification (direct or digest) was delivered. The
/// transitions are one-way and guarded in <see cref="UserJobAdMatch"/> — re-running the
/// Worker scan never re-notifies because an existing row in any non-<see cref="Pending"/>
/// status is skipped (idempotency by design; UNIQUE(UserId, JobAdId) is the spine).
/// Serialized by NAME (reorder-safe).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationStatus
{
    /// <summary>Persisted by the scan; no notification delivered yet.</summary>
    Pending,

    /// <summary>Picked up by the dispatch step; delivery in flight.</summary>
    Queued,

    /// <summary>The notification (direct or digest) was delivered.</summary>
    Sent,

    /// <summary>
    /// TD-114 (2026-06-25) — terminal failure state. The dispatch step claimed the row
    /// (<see cref="Queued"/>) but delivery never completed (a Resend send failed, or the
    /// account had no email); the row stranded with no recovery. The stranded-match reaper
    /// (a scheduled Worker job) moves a long-Queued row here so the strand is OBSERVABLE and
    /// terminal — it never re-sends (honouring the "never double-email &gt; never miss" stance)
    /// and is safe against the non-dev NullEmailSender (no send is attempted).
    /// <para>
    /// This is a NOTIFICATION-DELIVERY status, NOT a match-validity one. It is excluded from the
    /// DISPATCH queries (which filter <see cref="Pending"/>), exactly like <see cref="Sent"/>.
    /// But the in-app read surface (<c>GetMyMatches</c> / <c>GetMyNewMatchCount</c>) is
    /// deliberately status-AGNOSTIC, so a Failed match stays visible in /matchningar and still
    /// counts as new — a stranded match is still a real match; a failed EMAIL must not hide it.
    /// </para>
    /// </summary>
    Failed,
}
