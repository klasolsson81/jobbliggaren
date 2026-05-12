namespace JobbPilot.Domain.Waitlist;

public readonly record struct WaitlistEntryId(Guid Value)
{
    public static WaitlistEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
