using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

public sealed class ResumeLanguage : SmartEnum<ResumeLanguage>
{
    public static readonly ResumeLanguage Sv = new("Sv", 1);
    public static readonly ResumeLanguage En = new("En", 2);

    private ResumeLanguage(string name, int value) : base(name, value) { }
}
