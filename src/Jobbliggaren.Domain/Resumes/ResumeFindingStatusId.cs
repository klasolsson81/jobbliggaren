namespace Jobbliggaren.Domain.Resumes;

public readonly record struct ResumeFindingStatusId(Guid Value)
{
    public static ResumeFindingStatusId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
