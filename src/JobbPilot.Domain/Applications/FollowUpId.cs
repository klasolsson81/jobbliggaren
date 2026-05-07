namespace JobbPilot.Domain.Applications;

public readonly record struct FollowUpId(Guid Value)
{
    public static FollowUpId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
