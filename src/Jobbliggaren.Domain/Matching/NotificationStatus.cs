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
}
