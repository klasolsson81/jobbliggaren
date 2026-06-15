namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Per-section parse confidence (OQ5). The green/yellow/red model: a degraded
/// extraction is never silently presented as a confident one. Honest, not opaque —
/// each level is grounded by cited <see cref="SectionConfidence.Evidence"/>
/// (structural facts only — never the PII itself).
/// </summary>
public enum SectionConfidenceLevel
{
    /// <summary>Green — the section heading was found and at least one well-formed
    /// entry/value was extracted.</summary>
    Confident,

    /// <summary>Yellow — the heading was found but the content is partial or
    /// malformed (e.g. an empty block under a recognised heading).</summary>
    Degraded,

    /// <summary>Red — no heading and no content for this section. The honest
    /// "not found" state — never conflated with an empty-but-present section.</summary>
    NotFound,
}
