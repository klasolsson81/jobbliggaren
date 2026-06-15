namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// The confidence verdict for one parsed section, with cited evidence
/// (explainable-by-design, CLAUDE.md §5 — a confidence signal is never an opaque
/// number). <see cref="Evidence"/> states WHY the level landed where it did
/// (e.g. "heading 'arbetslivserfarenhet' matched; 3 entries") — structural facts
/// only, NEVER the PII content of the section.
/// </summary>
public sealed record SectionConfidence(
    ParsedSectionKind Kind,
    SectionConfidenceLevel Level,
    IReadOnlyList<string> Evidence);
