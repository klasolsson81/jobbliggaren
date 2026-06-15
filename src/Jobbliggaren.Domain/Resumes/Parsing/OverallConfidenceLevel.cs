namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Document-level parse confidence (OQ5), derived deterministically from the
/// per-section verdicts (see <see cref="ParseConfidence.FromSections"/>). There is
/// deliberately NO weighted numeric score — the per-section verdicts ARE the
/// confidence (parity with <c>MatchScore</c>'s no-opaque-total Goodhart guard).
/// </summary>
public enum OverallConfidenceLevel
{
    /// <summary>Key sections present and well-formed — the parse can be trusted as a
    /// starting point.</summary>
    Confident,

    /// <summary>Text was extracted but the structure is incomplete — surface the
    /// manual-review path (the user fills the gaps).</summary>
    Degraded,

    /// <summary>Extraction itself yielded no usable text (encrypted/scanned/corrupt).
    /// Routed straight to manual entry via <see cref="ParseFallbackReason"/>.</summary>
    Failed,
}
