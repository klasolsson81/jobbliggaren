using Ardalis.SmartEnum;

namespace JobbPilot.Domain.Applications;

public sealed class FollowUpOutcome : SmartEnum<FollowUpOutcome>
{
    public static readonly FollowUpOutcome Pending = new("Pending", 1);
    public static readonly FollowUpOutcome Responded = new("Responded", 2);
    public static readonly FollowUpOutcome NoResponse = new("NoResponse", 3);

    private FollowUpOutcome(string name, int value) : base(name, value) { }
}
