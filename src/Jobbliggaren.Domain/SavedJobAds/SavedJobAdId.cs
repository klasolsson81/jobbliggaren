namespace Jobbliggaren.Domain.SavedJobAds;

public readonly record struct SavedJobAdId(Guid Value)
{
    public static SavedJobAdId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
