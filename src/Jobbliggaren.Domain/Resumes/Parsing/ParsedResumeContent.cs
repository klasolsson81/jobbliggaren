namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Structured content of a parsed CV (F4-8). Net-new value object — NOT the canonical
/// <c>ResumeContent</c> (which has no språk section and whose strict validation is
/// hostile to a degraded parse, CTO Decision 1). Every collection is honest about
/// what the deterministic parser found; nothing is synthesised (CLAUDE.md §5).
/// This is CV-PII: persisted only via the field-encryption pipeline as a
/// JSON-serialised shadow (ADR 0074 Invariant 3, Form B).
/// </summary>
/// <remarks>
/// Equality on the collection properties is reference-based (record-generated
/// <c>Equals</c> compares list references, not elements) — acceptable because the
/// content is replaced wholesale, never mutated field-by-field (parity with
/// <c>ResumeContent</c>).
/// </remarks>
public sealed record ParsedResumeContent
{
    public ParsedContact Contact { get; init; }

    /// <summary>Profil / sammanfattning — the free-text summary, if found.</summary>
    public string? Profile { get; init; }

    public IReadOnlyList<ParsedExperience> Experience { get; init; }

    public IReadOnlyList<ParsedEducation> Education { get; init; }

    public IReadOnlyList<string> Skills { get; init; }

    public IReadOnlyList<string> Languages { get; init; }

    public ParsedResumeContent(
        ParsedContact contact,
        string? profile = null,
        IReadOnlyList<ParsedExperience>? experience = null,
        IReadOnlyList<ParsedEducation>? education = null,
        IReadOnlyList<string>? skills = null,
        IReadOnlyList<string>? languages = null)
    {
        Contact = contact;
        Profile = profile;
        Experience = experience ?? [];
        Education = education ?? [];
        Skills = skills ?? [];
        Languages = languages ?? [];
    }

    /// <summary>An empty parse — used when extraction failed and there is nothing to
    /// structure (the artifact still persists with a Failed confidence, OQ5).</summary>
    public static ParsedResumeContent Empty { get; } = new(ParsedContact.Empty);
}
