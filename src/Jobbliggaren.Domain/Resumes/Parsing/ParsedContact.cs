namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// The kontakt section of a parsed CV. Every field is optional — a deterministic
/// parse extracts what it can find and is honest about the rest (no synthesised
/// values, CLAUDE.md §5). This is CV-PII: it is persisted only inside the
/// field-encryption pipeline (ADR 0074 Invariant 3).
/// </summary>
public sealed record ParsedContact(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location)
{
    public static ParsedContact Empty { get; } = new(null, null, null, null);
}
