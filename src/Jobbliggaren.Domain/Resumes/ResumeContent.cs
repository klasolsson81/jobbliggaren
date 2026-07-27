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
/// <para>Fas 4b AppCopy-superset (ADR 0093 D1 / LRM ADR 0095): <see cref="Languages"/>
/// (sprak), <see cref="Sections"/> (dynamiska yrkesstyrda §7-sektioner) och
/// <see cref="SkillGroups"/> (kompetensgrupper — en referens-overlay över den platta
/// <see cref="Skills"/>-listan, ADR 0095 D-A) är alla <b>additiva och optionella</b>
/// (default tom lista). Den platta <see cref="Skills"/>-listan förblir den enda
/// auktoritativa kompetens-lagringen (bär <c>YearsExperience</c>). Att lägga till dessa
/// fält är en ren Form B expand/contract på serialiseringsnivån — ingen DDL, ingen
/// kolumnändring, eftersom <c>ResumeVersion.Content</c> är EF-<c>Ignore</c>:ad och
/// content_enc är opak (ADR 0095 D-D). Gamla ciphertext-payloads utan de nya nycklarna
/// deserialiseras rent till tomma listor (back-compat, ADR 0049 Beslut 5 read-tolerans).</para>
///
/// <para><see cref="Preamble"/> (#1060, 2026-07-27) lades till på exakt samma sätt och är
/// den enda superset-medlemmen som inte är en lista: den deserialiseras till <c>null</c>,
/// vilket är det semantiskt riktiga värdet för ett CV som aldrig importerades.</para>
/// </remarks>
public sealed record ResumeContent
{
    public PersonalInfo PersonalInfo { get; init; }
    public IReadOnlyList<Experience> Experiences { get; init; }
    public IReadOnlyList<Education> Educations { get; init; }

    /// <summary>
    /// The flat, authoritative skill set (carries <c>YearsExperience</c>). The single
    /// source of truth for "what skills does this CV have" — <see cref="SkillGroups"/> only
    /// references names present here (ADR 0095 D-A, DRY).
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

    /// <summary>
    /// The verbatim, UNCLASSIFIED text an IMPORTED CV carried above its first heading (#844,
    /// ADR 0109; projected onto the canonical CV by #1060's CTO-bind, 2026-07-27). <c>null</c>
    /// on a template-origin CV — by construction, not from inability: the linearizer emits
    /// every section under a heading (ADR 0097 §2), so an app-built CV has no region above its
    /// first one. <c>null</c> is also what a pre-field ciphertext payload deserialises to, and
    /// that is the semantically correct value rather than merely a tolerated one.
    ///
    /// <para><b>Write-once.</b> The ONLY ingress is <see cref="Resume.CreateFromParsed"/>;
    /// <see cref="Resume.UpdateMasterContent"/> carries the existing value forward and ignores
    /// whatever the transport says, so no user-authored preamble exists or can exist and the
    /// classify step stays FAS-DEFERRED structurally rather than by convention (ADR 0109
    /// Amendment 2026-07-18, ADR 0112). It is the identity projection of
    /// <c>ParsedResumeContent.Preamble</c> — the only mapping that neither MINTS a section
    /// identity (ADR 0109 §1) nor DROPS the text (§3).</para>
    ///
    /// <para><b>Never rendered.</b> Not by <c>ResumeContentLinearizer</c>, not by the PDF
    /// renderer, not in the ATS view — an unclassified page number, OCR header or address
    /// block must never appear in a document the user sends to employers. ADR 0109's accepted
    /// junk cost is DISPLAY, never RENDER. Pinned by test; nothing in those paths reflects, so
    /// the pin is what keeps it true rather than what makes it true.</para>
    /// </summary>
    public string? Preamble { get; init; }

    public ResumeContent(
        PersonalInfo personalInfo,
        IReadOnlyList<Experience>? experiences = null,
        IReadOnlyList<Education>? educations = null,
        IReadOnlyList<Skill>? skills = null,
        string? summary = null,
        IReadOnlyList<SpokenLanguage>? languages = null,
        IReadOnlyList<SkillGroup>? skillGroups = null,
        IReadOnlyList<ResumeSection>? sections = null,
        string? preamble = null)
    {
        PersonalInfo = personalInfo;
        Experiences = experiences ?? [];
        Educations = educations ?? [];
        Skills = skills ?? [];
        Summary = summary;
        Languages = languages ?? [];
        SkillGroups = skillGroups ?? [];
        Sections = sections ?? [];
        Preamble = preamble;
    }

    public static ResumeContent Empty(string fullName) =>
        new(new PersonalInfo(fullName, null, null, null));
}
