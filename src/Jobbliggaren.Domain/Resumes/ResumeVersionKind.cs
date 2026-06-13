using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

public sealed class ResumeVersionKind : SmartEnum<ResumeVersionKind>
{
    public static readonly ResumeVersionKind Master = new("Master", 1);
    public static readonly ResumeVersionKind Tailored = new("Tailored", 2);

    private ResumeVersionKind(string name, int value) : base(name, value) { }
}
