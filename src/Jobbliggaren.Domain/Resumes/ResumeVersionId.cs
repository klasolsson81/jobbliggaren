namespace Jobbliggaren.Domain.Resumes;

public readonly record struct ResumeVersionId(Guid Value)
{
    public static ResumeVersionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
