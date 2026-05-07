using Ardalis.SmartEnum;

namespace JobbPilot.Domain.Applications;

public sealed class FollowUpChannel : SmartEnum<FollowUpChannel>
{
    public static readonly FollowUpChannel Email = new("Email", 1);
    public static readonly FollowUpChannel LinkedIn = new("LinkedIn", 2);
    public static readonly FollowUpChannel Phone = new("Phone", 3);
    public static readonly FollowUpChannel Other = new("Other", 4);

    private FollowUpChannel(string name, int value) : base(name, value) { }
}
