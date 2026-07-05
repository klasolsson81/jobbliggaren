using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// The single highest-priority reason an application needs action now, surfaced
/// in the /ansokningar "Kräver åtgärd" section (design §11 "Urgensregler",
/// superseding the ADR 0085 §3 five-signal rule; CTO-bound 2026-07-05 for epik
/// #630 PR 4). An application surfaces under at most one reason — its
/// highest-priority firing signal.
///
/// <para>
/// Declaration order after <see cref="None"/> mirrors the locked priority order
/// (design §11): offer → overdue follow-up → draft deadline → ghost-suggest →
/// no-response nudge → silent-after-interview. The evaluator returns the first
/// signal that fires in this order, and the FE <c>ATTENTION_SIGNAL_ORDER</c>
/// mirrors it. <see cref="GhostSuggested"/> (≥ GhostSuggestDays) is deliberately
/// ranked ABOVE <see cref="NoResponseNudge"/> (≥ NoResponseNudgeDays) because the
/// larger no-response window subsumes the smaller one — checking the nudge first
/// would make the ghost-suggest signal unreachable (a correctness constraint, not
/// a presentation preference).
/// </para>
///
/// <para>
/// Serialized by NAME, not ordinal (<c>[JsonStringEnumConverter]</c>, parity with
/// <c>MatchGrade</c> / <c>DigestCadence</c> / <c>NotificationStatus</c>): the read
/// DTO carries this signal across the API as e.g. <c>"GhostSuggested"</c>, which
/// the /ansokningar Zod schema binds as a string union. Without the converter the
/// wire value would be the int <c>0</c> (None) and the FE enum parse would break.
/// The signal is never persisted — it defaults to <see cref="None"/> and is
/// re-stamped in-memory on the read side — so the rename in PR 4 carries no
/// migration and no stored value to reconcile.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApplicationAttentionSignal
{
    /// <summary>The application does not currently require action.</summary>
    None = 0,

    /// <summary>Signal 1 — an offer is awaiting the applicant's reply (status OfferReceived). Fires unconditionally.</summary>
    OfferAwaitingReply,

    /// <summary>Signal 2 — a scheduled follow-up is Pending and its time has passed.</summary>
    OverdueFollowUp,

    /// <summary>Signal 3 — a Draft whose application deadline is approaching and not yet passed.</summary>
    DraftDeadlineApproaching,

    /// <summary>Signal 4 — submitted/acknowledged with no response for at least the ghost-suggest window (effective wait ≥ GhostSuggestDays); the card is offered for manual Ghosted marking.</summary>
    GhostSuggested,

    /// <summary>Signal 5 — submitted/acknowledged with no response long enough (effective wait ≥ NoResponseNudgeDays) that a proactive follow-up is due.</summary>
    NoResponseNudge,

    /// <summary>Signal 6 — an Interviewing application silent (no status change) for at least the silent-after-interview window (effective wait ≥ SilentAfterInterviewDays).</summary>
    SilentAfterInterview,
}
