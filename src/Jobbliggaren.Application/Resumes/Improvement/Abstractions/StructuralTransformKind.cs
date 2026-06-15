namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The pure total transform a <see cref="StructuralTransformProvenance"/> declares — the
/// "show your work" token (Fas 4 STEG 10, F4-10). Every <c>After</c> produced under a
/// structural provenance MUST be reproducible by re-running this transform on <c>Before</c>
/// (enforced by <see cref="ProposedChange.FromStructuralOp"/>), so the determinism can never
/// smuggle in synthesised text (CLAUDE.md §5). A LOCKED set (arch-pinned).
/// </summary>
public enum StructuralTransformKind
{
    /// <summary>Reformat a recognised date/period to the canonical form.</summary>
    ReformatDate,

    /// <summary>Normalise a section heading's letter case (a pure case transform of the text).</summary>
    NormalizeHeadingCase,

    /// <summary>Remove a personnummer (pure removal; no rewritten text).</summary>
    RemovePersonnummer,

    /// <summary>Remove a profile-photo reference (pure removal).</summary>
    RemovePhotoReference,

    /// <summary>Remove a GPA/grade reference (pure removal).</summary>
    RemoveGpa,

    /// <summary>Strip non-standard glyphs an ATS parser mangles (pure removal of ornament chars).</summary>
    StripNonStandardChars,

    /// <summary>Reorder CV sections to a recommended order (a pure structural move).</summary>
    ReorderSection,
}
