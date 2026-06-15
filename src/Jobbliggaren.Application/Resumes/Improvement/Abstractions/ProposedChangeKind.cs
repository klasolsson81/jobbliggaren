namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The kind of improvement a <see cref="ProposedChange"/> proposes (Fas 4 STEG 10, F4-10,
/// ADR 0071/0074 — NO AI/LLM). A LOCKED set (arch-pinned, parity
/// <c>CriterionVerdict</c>): each member maps to exactly one deterministic transform in
/// Infrastructure. The engine PROPOSES; the user APPROVES — a diff is never auto-applied,
/// and no member ever synthesises prose (CLAUDE.md §5).
/// </summary>
public enum ProposedChangeKind
{
    /// <summary>A cliché phrase replaced by its knowledge-bank better-alternative (A7).</summary>
    ClicheReplacement,

    /// <summary>A weak opening verb upgraded to its knowledge-bank strong verb (A2/C3).</summary>
    WeakVerbUpgrade,

    /// <summary>A non-standard date/period format flagged for canonical reformatting (B6).</summary>
    DateNormalization,

    /// <summary>Section order flagged against a recommended order (B1). Not assessed in v1.</summary>
    SectionReorder,

    /// <summary>A non-standard-cased section heading normalised to standard case (D6).</summary>
    HeadingNormalization,

    /// <summary>A flagged personnummer removed (B4, GDPR). A pure removal — never echoes the value.</summary>
    PersonnummerStrip,

    /// <summary>A profile photo reference removed (default OFF for SE). Not assessed in v1.</summary>
    PhotoStrip,

    /// <summary>A GPA/grade reference removed from an education entry (SE-market convention).</summary>
    GpaStrip,

    /// <summary>Non-standard glyphs an ATS parser mangles stripped (ATS profile only).</summary>
    AtsSanitization,
}
