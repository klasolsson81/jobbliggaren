using Ardalis.SmartEnum;

namespace JobbPilot.Domain.Applications;

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

    private readonly HashSet<ApplicationStatus> _allowedTransitions = [];
    public IReadOnlySet<ApplicationStatus> AllowedTransitions => _allowedTransitions;

    private ApplicationStatus(string name, int value) : base(name, value) { }

    static ApplicationStatus()
    {
        Draft._allowedTransitions.Add(Submitted);

        Submitted._allowedTransitions.Add(Acknowledged);
        Submitted._allowedTransitions.Add(Rejected);
        Submitted._allowedTransitions.Add(Withdrawn);

        Acknowledged._allowedTransitions.Add(InterviewScheduled);
        Acknowledged._allowedTransitions.Add(Rejected);
        Acknowledged._allowedTransitions.Add(Withdrawn);

        InterviewScheduled._allowedTransitions.Add(Interviewing);
        InterviewScheduled._allowedTransitions.Add(Withdrawn);

        Interviewing._allowedTransitions.Add(OfferReceived);
        Interviewing._allowedTransitions.Add(Rejected);
        Interviewing._allowedTransitions.Add(Withdrawn);

        OfferReceived._allowedTransitions.Add(Accepted);
        OfferReceived._allowedTransitions.Add(Rejected);
        OfferReceived._allowedTransitions.Add(Withdrawn);

        // Ghosted kan reaktiveras manuellt
        Ghosted._allowedTransitions.Add(Submitted);

        // Accepted, Rejected, Withdrawn: terminaltillstånd — inga transitions
    }
}
