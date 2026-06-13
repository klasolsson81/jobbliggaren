namespace Jobbliggaren.Domain.JobAds;

public readonly record struct JobAdId(Guid Value)
{
    public static JobAdId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
