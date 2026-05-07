namespace JobbPilot.Domain.Applications;

public readonly record struct ApplicationNoteId(Guid Value)
{
    public static ApplicationNoteId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
