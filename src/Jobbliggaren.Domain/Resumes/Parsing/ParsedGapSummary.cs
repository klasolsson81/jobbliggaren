namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Which confirm-tasks the parse left complete vs missing (Fas 4b PR-8, CTO-bind Q5) —
/// the data behind the hub action card's "X av Y uppgifter klara" meter and the
/// Slutför-guide's shared task definition (single source: the meter and the guide's
/// step gate must never disagree). Presence booleans ONLY — non-PII by shape (no CV
/// text, parity <see cref="ParseConfidence"/>/<see cref="CvLayoutMetrics"/>), computed
/// once at import where the plaintext already exists (ADR 0059 denormalization — the
/// hub list path never decrypts CV-PII). Null on pre-PR-8 rows: an honest "not
/// computed", never backfilled by guessing.
/// </summary>
public sealed record ParsedGapSummary(
    bool HasFullName,
    bool HasEmail,
    bool HasPhone,
    bool HasLocation,
    bool HasProfile,
    bool HasExperience,
    bool HasEducation,
    bool HasSkills,
    bool HasLanguages)
{
    /// <summary>
    /// Derives the presence flags from parsed content. Whitespace-only extraction noise
    /// counts as missing (the guide would still ask for it), empty collections count as
    /// missing sections.
    /// </summary>
    public static ParsedGapSummary FromContent(ParsedResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ParsedGapSummary(
            HasFullName: Present(content.Contact.FullName),
            HasEmail: Present(content.Contact.Email),
            HasPhone: Present(content.Contact.Phone),
            HasLocation: Present(content.Contact.Location),
            HasProfile: Present(content.Profile),
            HasExperience: content.Experience.Count > 0,
            HasEducation: content.Education.Count > 0,
            HasSkills: content.Skills.Count > 0,
            HasLanguages: content.Languages.Count > 0);
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}
