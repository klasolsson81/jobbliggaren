namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Which gap-bridging policy <see cref="PersonnummerTextNormalizer.Normalize"/> applies.
/// There is deliberately NO default: a call site states which kind of text it holds, because
/// the two policies are not orderable by strength — each is safer than the other on the input
/// it was measured against (#1415, ADR 0134, which owns the ground).
/// </summary>
public enum PersonnummerGapProfile
{
    /// <summary>
    /// Text a machine extracted from a document. Bridges 0-2 VISIBLE columns of
    /// <c>\p{Zs}</c>/tab and never a line break.
    ///
    /// <para>A line break is a field boundary here, not an OCR gap. Bridging one is measurably
    /// dangerous rather than merely inelegant: a date column stacked above a 4-digit run
    /// satisfies the date gate with certainty, leaving only Luhn — an order of magnitude worse
    /// than the arbitrary-digit case the original bound was argued from.
    /// PersonnummerBridgeCollisionRateTests regenerates both rates.</para>
    ///
    /// <para>Used by CV import (body and file name), resume content, the auto-promote gate,
    /// <c>JobSeeker.DisplayName</c> and <c>Resume.Name</c> — six surfaces, seven invocations,
    /// pinned by <c>PersonnummerGapProfileCallSiteTests</c>. The last two are single-line
    /// values and are candidates for the other profile; that is a follow-up with its own bearer
    /// analysis, not an oversight (ADR 0134). The file name is NOT a candidate: it is redacted
    /// downstream on a path that keeps the narrow bridge, so flagging it wider would produce
    /// flagged-but-unmasked (ADR 0134 D8).</para>
    /// </summary>
    ExtractedDocumentText,

    /// <summary>
    /// A single-line value a human typed into one input. Bridges 0-8 columns of any whitespace
    /// or C0/C1 control character, line breaks included.
    ///
    /// <para>The line-break hazard above cannot arise: the value has no line structure, so a
    /// break inside it is stuffing rather than a field boundary. A missed personnummer here
    /// persists in plaintext and renders back verbatim, while an over-flag costs one silently
    /// uncaptured search — an asymmetry ADR 0134 D2 states and this profile is chosen against.</para>
    ///
    /// <para>Used by the <c>?q=</c> job-search axis alone. Gaps above eight characters are a
    /// declared residual (ADR 0134 R3), not an oversight.</para>
    /// </summary>
    SingleLineUserInput,
}
