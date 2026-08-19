namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Which gap-bridging policy <see cref="PersonnummerTextNormalizer.Normalize"/> applies.
/// There is deliberately NO default: a call site states which kind of text it holds, because
/// the two policies are not orderable by strength — each is safer than the other on the input
/// it was measured against (#1415, ADR 0134).
/// </summary>
public enum PersonnummerGapProfile
{
    /// <summary>
    /// Text a machine extracted from a document (CV body, file name, resume content, promote
    /// label). Bridges 0–2 VISIBLE columns of <c>\p{Zs}</c>/tab and never a line break.
    ///
    /// <para>A line break is a field boundary here, not an OCR gap, and bridging one is
    /// measurably dangerous rather than merely inelegant: an <c>YYYYMMDD</c> date column
    /// stacked above a 4-digit run passes the date gate with certainty, so only Luhn remains
    /// and the collision rate is ~1 in 10 — not the ~1 in 136 that two arbitrary numbers give
    /// (ADR 0134's Monte Carlo). <c>ResumeContentPersonnummerGuard.CollectFreeText</c>
    /// additionally joins SEPARATE DTO fields with <c>AppendLine</c>, so a line-crossing bridge
    /// would span field boundaries that share no meaning at all.</para>
    /// </summary>
    ExtractedDocumentText,

    /// <summary>
    /// A single-line value a human typed into one input (the <c>?q=</c> search box). Bridges
    /// 0–8 columns of any whitespace or C0/C1 control character, line breaks included.
    ///
    /// <para>The line-break hazard above cannot arise: the value has no line structure to
    /// mean anything, so a break inside it is stuffing rather than a field boundary. The
    /// value is persisted in plaintext outside ADR 0049's envelope and re-rendered verbatim
    /// as the <c>/sokningar</c> row label, and a missed personnummer there is a PII leak
    /// against a bearer who may be a third party, while an over-flag costs one silently
    /// uncaptured search.</para>
    /// </summary>
    SingleLineUserInput,
}
