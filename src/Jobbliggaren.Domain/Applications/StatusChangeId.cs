namespace Jobbliggaren.Domain.Applications;

public readonly record struct StatusChangeId(Guid Value)
{
    public static StatusChangeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
