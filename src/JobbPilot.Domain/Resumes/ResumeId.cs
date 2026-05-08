namespace JobbPilot.Domain.Resumes;

public readonly record struct ResumeId(Guid Value)
{
    public static ResumeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
