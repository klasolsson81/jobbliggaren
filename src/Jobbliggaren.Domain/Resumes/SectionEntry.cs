namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One entry inside a dynamic profession-driven CV section (Fas 4b AppCopy superset,
/// ADR 0093 D1 / LRM ADR 0094 D-B). <paramref name="Title"/> is the entry heading (e.g. a
/// project name, a certification); <paramref name="Lines"/> are the body lines (design
/// handoff §5.2 "titel/underrad/metabricka" collapses into title + lines — the semantic
/// shape, never the visual one, which is a rendering concern). Both are CV-PII free text
/// scanned by <c>ResumeContentPersonnummerGuard</c>.
/// </summary>
public sealed record SectionEntry(string Title, IReadOnlyList<string> Lines);
