namespace Jobbliggaren.Domain.SavedSearches;

public readonly record struct SavedSearchId(Guid Value)
{
    public static SavedSearchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
