namespace Jobbliggaren.Domain.RecentJobSearches;

public readonly record struct RecentJobSearchId(Guid Value)
{
    public static RecentJobSearchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
