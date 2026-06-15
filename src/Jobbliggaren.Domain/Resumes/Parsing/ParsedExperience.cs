namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// One arbetslivserfarenhet entry from a parsed CV. Best-effort structured fields
/// (all optional — a deterministic parse cannot reliably recover every field) plus
/// the verbatim <see cref="RawText"/> of the entry so the F4-9 review engine can
/// cite the exact span (ADR 0074 Invariant 2). CV-PII — persisted encrypted only.
/// </summary>
public sealed record ParsedExperience(
    string? Title,
    string? Organization,
    string? Period,
    string RawText);
