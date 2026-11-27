namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// One arbetslivserfarenhet entry from a parsed CV. Best-effort structured fields
/// (all optional — a deterministic parse cannot reliably recover every field) plus
/// the verbatim <see cref="RawText"/> of the entry so the F4-9 review engine can
/// cite the exact span (ADR 0074 Invariant 2). CV-PII — persisted encrypted only.
/// </summary>
/// <param name="Description">
/// The entry's description lines joined with a newline (#2353): the block without its header, its
/// period lines and its organisation line, each line verbatim. Null when the entry has none, and on
/// an entry parsed before the segmenter read them; the review reads both alike.
/// </param>
public sealed record ParsedExperience(
    string? Title,
    string? Organization,
    string? Period,
    string RawText,
    string? Description = null);
