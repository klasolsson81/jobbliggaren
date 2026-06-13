namespace Jobbliggaren.Domain.JobSeekers;

public readonly record struct JobSeekerId(Guid Value)
{
    public static JobSeekerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
