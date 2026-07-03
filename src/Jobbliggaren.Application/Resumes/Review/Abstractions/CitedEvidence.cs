namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The evidence that grounds a CV-review verdict (Fas 4 STEG 9, F4-9 — ADR 0074
/// Invariant 2: every PASS/WARN/FAIL MUST cite what grounds it; a verdict without
/// cited evidence is invalid). A closed, two-channel discriminated form
/// (architect-bound; Klas decision 2026-06-15):
/// <list type="bullet">
/// <item><see cref="TextSpanEvidence"/> — for verdicts grounded in PRESENT CV text
/// (the deficient/offending span is quoted): A1 number-less bullets, A7 cliché spans,
/// C3 passive constructions, …</item>
/// <item><see cref="StructuralEvidence"/> — for ABSENCE/structural verdicts where there
/// is no span to quote (a non-PII fact, parity with <c>SectionConfidence.Evidence</c>):
/// "e-post saknas" (B3), the personnummer count (B4, never the raw value, Inv.1), …</item>
/// </list>
/// The invariant's intent (no hallucination — the determinism can only cite what is
/// present/true; explainability; audit) is the bar, not literal text-spans only.
/// </summary>
public abstract record CitedEvidence;

/// <summary>
/// A cited span of the CV's raw text: the character offset + length into
/// <c>ParsedResume.RawText</c> (or an entry's raw text) plus the verbatim
/// <paramref name="Quote"/>, and an optional human-readable note on why it grounds the
/// verdict. Used when the verdict is grounded in present text.
/// </summary>
public sealed record TextSpanEvidence(TextSpan Span, string? Note) : CitedEvidence;

/// <summary>
/// A non-PII structural observation grounding an absence/structural verdict (e.g.
/// "kontaktsektion hittad; e-post saknas", "1 personnummer hittat"). Never echoes raw
/// CV-PII — parity with the structural-facts contract of <c>SectionConfidence.Evidence</c>.
/// </summary>
public sealed record StructuralEvidence(string Observation) : CitedEvidence;

/// <summary>
/// A span of CV text: <paramref name="Start"/> (0-based char index) and
/// <paramref name="Length"/> into the source string, with the verbatim
/// <paramref name="Quote"/> so the UI can highlight without re-reading the CV-PII.
/// <para><see cref="Start"/> is <see cref="NotLocated"/> when the quote could not be located
/// in its source — an honest "position unknown", never a fabricated offset 0 (#478 Low). The
/// <paramref name="Quote"/> is always the verbatim ground truth; the UI highlights by text, so
/// a NotLocated span still renders correctly.</para>
/// </summary>
public sealed record TextSpan(int Start, int Length, string Quote)
{
    /// <summary>Sentinel <see cref="Start"/> for a quote that could not be located in its
    /// source string. The evidence still carries the verbatim <see cref="Quote"/>, but no
    /// trustworthy offset — so "not located" is never masked as the valid position 0 (#478
    /// Low; parity with the fail-loud discipline the rest of the parse pipeline already keeps).</summary>
    public const int NotLocated = -1;
}
