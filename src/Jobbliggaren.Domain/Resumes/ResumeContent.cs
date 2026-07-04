namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Strukturerat CV-innehåll. Persisteras krypterat i resume_versions.content_enc
/// (ADR 0049 Form B; legacy plaintext-content nullställd vid cutover #507a).
/// </summary>
/// <remarks>
/// OBS: equality på collection-properties är reference-baserad (inte value-baserad)
/// — record-genererad Equals jämför IReadOnlyList&lt;T&gt;-referenser, inte element.
/// Detta är acceptabelt eftersom ResumeContent muteras genom hela-ersättning, inte
/// delfält. Två lika "logiska" innehåll är inte automatiskt Equals.
/// </remarks>
public sealed record ResumeContent
{
    public PersonalInfo PersonalInfo { get; init; }
    public IReadOnlyList<Experience> Experiences { get; init; }
    public IReadOnlyList<Education> Educations { get; init; }
    public IReadOnlyList<Skill> Skills { get; init; }
    public string? Summary { get; init; }

    public ResumeContent(
        PersonalInfo personalInfo,
        IReadOnlyList<Experience>? experiences = null,
        IReadOnlyList<Education>? educations = null,
        IReadOnlyList<Skill>? skills = null,
        string? summary = null)
    {
        PersonalInfo = personalInfo;
        Experiences = experiences ?? [];
        Educations = educations ?? [];
        Skills = skills ?? [];
        Summary = summary;
    }

    public static ResumeContent Empty(string fullName) =>
        new(new PersonalInfo(fullName, null, null, null));
}
