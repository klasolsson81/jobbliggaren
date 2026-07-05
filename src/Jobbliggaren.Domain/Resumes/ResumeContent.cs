namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Strukturerat CV-innehåll. Persisteras krypterat i resume_versions.content_enc
/// (ADR 0049 Form B; legacy plaintext-content nullställd vid cutover #507a).
/// </summary>
/// <remarks>
/// <para>OBS: equality på collection-properties är reference-baserad (inte value-baserad)
/// — record-genererad Equals jämför IReadOnlyList&lt;T&gt;-referenser, inte element.
/// Detta är acceptabelt eftersom ResumeContent muteras genom hela-ersättning, inte
/// delfält. Två lika "logiska" innehåll är inte automatiskt Equals.</para>
///
/// <para>Fas 4b AppCopy-superset (ADR 0093 D1 / LRM ADR 0094): <see cref="Languages"/>
/// (sprak), <see cref="Sections"/> (dynamiska yrkesstyrda §7-sektioner) och
/// <see cref="SkillGroups"/> (kompetensgrupper — en referens-overlay över den platta
/// <see cref="Skills"/>-listan, ADR 0094 D-A) är alla <b>additiva och optionella</b>
/// (default tom lista). Den platta <see cref="Skills"/>-listan förblir den enda
/// auktoritativa kompetens-lagringen (bär <c>YearsExperience</c>). Att lägga till dessa
/// fält är en ren Form B expand/contract på serialiseringsnivån — ingen DDL, ingen
/// kolumnändring, eftersom <c>ResumeVersion.Content</c> är EF-<c>Ignore</c>:ad och
/// content_enc är opak (ADR 0094 D-D). Gamla ciphertext-payloads utan de nya nycklarna
/// deserialiseras rent till tomma listor (back-compat, ADR 0049 Beslut 5 read-tolerans).</para>
/// </remarks>
public sealed record ResumeContent
{
    public PersonalInfo PersonalInfo { get; init; }
    public IReadOnlyList<Experience> Experiences { get; init; }
    public IReadOnlyList<Education> Educations { get; init; }

    /// <summary>
    /// The flat, authoritative skill set (carries <c>YearsExperience</c>). The single
    /// source of truth for "what skills does this CV have" — <see cref="SkillGroups"/> only
    /// references names present here (ADR 0094 D-A, DRY).
    /// </summary>
    public IReadOnlyList<Skill> Skills { get; init; }
    public string? Summary { get; init; }

    /// <summary>Spoken languages (sprak, Fas 4b superset). Empty for legacy content.</summary>
    public IReadOnlyList<SpokenLanguage> Languages { get; init; }

    /// <summary>
    /// Grouped-skills overlay (kompetensgrupper, Fas 4b superset). A presentation grouping
    /// over <see cref="Skills"/>; never a second skill store. Empty for legacy content.
    /// </summary>
    public IReadOnlyList<SkillGroup> SkillGroups { get; init; }

    /// <summary>
    /// Dynamic profession-driven sections beyond the four standard ones (sektioner, Fas 4b
    /// superset). Empty for legacy content.
    /// </summary>
    public IReadOnlyList<ResumeSection> Sections { get; init; }

    public ResumeContent(
        PersonalInfo personalInfo,
        IReadOnlyList<Experience>? experiences = null,
        IReadOnlyList<Education>? educations = null,
        IReadOnlyList<Skill>? skills = null,
        string? summary = null,
        IReadOnlyList<SpokenLanguage>? languages = null,
        IReadOnlyList<SkillGroup>? skillGroups = null,
        IReadOnlyList<ResumeSection>? sections = null)
    {
        PersonalInfo = personalInfo;
        Experiences = experiences ?? [];
        Educations = educations ?? [];
        Skills = skills ?? [];
        Summary = summary;
        Languages = languages ?? [];
        SkillGroups = skillGroups ?? [];
        Sections = sections ?? [];
    }

    public static ResumeContent Empty(string fullName) =>
        new(new PersonalInfo(fullName, null, null, null));
}
