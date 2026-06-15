namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// One utbildning entry from a parsed CV. Best-effort structured fields (all optional)
/// plus the verbatim <see cref="RawText"/> for F4-9 span citation (ADR 0074
/// Invariant 2). CV-PII — persisted encrypted only.
/// </summary>
public sealed record ParsedEducation(
    string? Institution,
    string? Degree,
    string? Period,
    string RawText);
