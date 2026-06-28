namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// The single highest-priority reason an application needs action now, surfaced
/// in the /ansokningar "Kräver åtgärd" section (ADR 0085 §3). An application
/// surfaces under at most one reason — its highest-priority firing signal.
///
/// <para>
/// Declaration order after <see cref="None"/> mirrors the locked priority order
/// (ADR 0085 §3): offer → overdue follow-up → draft deadline → no response →
/// proactive nudge. The evaluator returns the first signal that fires in this
/// order.
/// </para>
/// </summary>
public enum ApplicationAttentionSignal
{
    /// <summary>The application does not currently require action.</summary>
    None = 0,

    /// <summary>Signal 1 — an offer is awaiting the applicant's reply (status OfferReceived).</summary>
    OfferAwaitingReply,

    /// <summary>Signal 2 — a scheduled follow-up is Pending and its time has passed.</summary>
    OverdueFollowUp,

    /// <summary>Signal 5 — a Draft whose application deadline is approaching and not yet passed.</summary>
    DraftDeadlineApproaching,

    /// <summary>Signal 4 — submitted/acknowledged with no status change for longer than the ghosted threshold.</summary>
    NoResponseLong,

    /// <summary>Signal 3 — submitted/acknowledged long enough that a proactive follow-up is due.</summary>
    ProactiveFollowUpNudge,
}
