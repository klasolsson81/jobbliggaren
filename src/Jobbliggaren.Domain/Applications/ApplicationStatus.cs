using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Applications;

public sealed class ApplicationStatus : SmartEnum<ApplicationStatus>
{
    public static readonly ApplicationStatus Draft = new("Draft", 1);
    public static readonly ApplicationStatus Submitted = new("Submitted", 2);
    public static readonly ApplicationStatus Acknowledged = new("Acknowledged", 3);
    public static readonly ApplicationStatus InterviewScheduled = new("InterviewScheduled", 4);
    public static readonly ApplicationStatus Interviewing = new("Interviewing", 5);
    public static readonly ApplicationStatus OfferReceived = new("OfferReceived", 6);
    public static readonly ApplicationStatus Accepted = new("Accepted", 7);
    public static readonly ApplicationStatus Rejected = new("Rejected", 8);
    public static readonly ApplicationStatus Withdrawn = new("Withdrawn", 9);
    public static readonly ApplicationStatus Ghosted = new("Ghosted", 10);

    // #648: closed for follow-up activity — the three terminal kanban statuses
    // (Accepted/Rejected/Withdrawn) PLUS Ghosted. Ghosted is deliberately included (no activity on
    // a ghosted thread) even though it is NOT terminal (it is reactivatable) — this set spans wider
    // than the aggregate's three-terminal check on purpose.
    private static readonly HashSet<ApplicationStatus> ClosedForActivityStatuses =
        [Accepted, Rejected, Withdrawn, Ghosted];

    /// <summary>
    /// True for a status closed to follow-up activity: the three terminals plus Ghosted. The single
    /// source for the Application aggregate's AddFollowUp/LogFollowUp guards and the OverdueFollowUp
    /// attention signal (#648) — so a closed thread neither accepts a new follow-up nor keeps
    /// nudging about a lingering one.
    /// </summary>
    public bool IsClosedForActivity => ClosedForActivityStatuses.Contains(this);

    private readonly HashSet<ApplicationStatus> _recommendedNext = [];

    /// <summary>
    /// The conventional forward/closing transitions from this status. ADR 0092 D3:
    /// this is ADVISORY ONLY — it is NO LONGER a guard.
    /// <see cref="Application.TransitionTo"/> permits any of the ten statuses as a
    /// target (free transitions; the removed grind is replaced by an undo-toast +
    /// full audit + the StatusChange timeline). Do not reintroduce enforcement off
    /// this set — that is the invariant this rename makes explicit. Intended as the
    /// SSOT for the UI hint (the "Flytta till {nästa steg}" default + the
    /// avsluta/parkera options), surfaced to the FE in a later epic PR; it has no
    /// C# consumer today (the FE currently mirrors the graph in status.ts).
    /// </summary>
    public IReadOnlySet<ApplicationStatus> RecommendedNextStatuses => _recommendedNext;

    private ApplicationStatus(string name, int value) : base(name, value) { }

    static ApplicationStatus()
    {
        // The conventional pipeline path (forward step + the usual closing moves),
        // surfaced as UI hints only. Free transitions (ADR 0092 D3) mean the user
        // may move to ANY status regardless of what is listed here.
        Draft._recommendedNext.Add(Submitted);

        Submitted._recommendedNext.Add(Acknowledged);
        Submitted._recommendedNext.Add(Rejected);
        Submitted._recommendedNext.Add(Withdrawn);

        Acknowledged._recommendedNext.Add(InterviewScheduled);
        Acknowledged._recommendedNext.Add(Rejected);
        Acknowledged._recommendedNext.Add(Withdrawn);

        InterviewScheduled._recommendedNext.Add(Interviewing);
        InterviewScheduled._recommendedNext.Add(Withdrawn);

        Interviewing._recommendedNext.Add(OfferReceived);
        Interviewing._recommendedNext.Add(Rejected);
        Interviewing._recommendedNext.Add(Withdrawn);

        OfferReceived._recommendedNext.Add(Accepted);
        OfferReceived._recommendedNext.Add(Rejected);
        OfferReceived._recommendedNext.Add(Withdrawn);

        // Ghosted's conventional move is reactivation to Submitted.
        Ghosted._recommendedNext.Add(Submitted);

        // Accepted, Rejected, Withdrawn: no conventional onward step (terminal in
        // the happy path), but free transitions still permit reopening them.
    }
}
