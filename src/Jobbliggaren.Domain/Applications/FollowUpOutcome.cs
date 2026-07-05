using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Applications;

public sealed class FollowUpOutcome : SmartEnum<FollowUpOutcome>
{
    public static readonly FollowUpOutcome Pending = new("Pending", 1);
    public static readonly FollowUpOutcome Responded = new("Responded", 2);
    public static readonly FollowUpOutcome NoResponse = new("NoResponse", 3);

    /// <summary>
    /// A completed contact logged today via the "Logga uppföljning" quick action
    /// (ADR 0092 D4/D5) — "I reached out, awaiting reply". Deliberately NOT
    /// <see cref="Pending"/>, so a logged follow-up never fires the OverdueFollowUp
    /// attention signal (which keys on a Pending follow-up whose ScheduledAt has
    /// passed). It exists to be listed on the timeline and to reset the effective
    /// wait counter (via Application.LastFollowUpAt).
    /// </summary>
    public static readonly FollowUpOutcome Logged = new("Logged", 4);

    private FollowUpOutcome(string name, int value) : base(name, value) { }
}
