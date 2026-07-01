using System.Text.RegularExpressions;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Produces a transient SCAN-COPY of free text in which a narrow,
/// personnummer-shaped digit–gap–digit pattern has its bridging whitespace removed,
/// so the context-free <see cref="PersonnummerScanner"/> can FLAG spaced/OCR-gapped
/// forms (e.g. <c>19811218 9876</c>, <c>811218 9876</c>). This is the F4-8 call-site
/// fix for the spaced-form false-negative F4-1 deferred here (ADR 0074 Invariant 1).
///
/// <para><b>Why the fix lives here, not in the scanner:</b> adding <c>\s</c> to the
/// context-free scanner regex would bridge unrelated adjacent numbers across a CV
/// full of dates and phone numbers (false positives). This normalizer only widens
/// candidate <i>shaping</i>; the safety stays in the UNCHANGED validation layer —
/// <see cref="Personnummer.TryParse"/> still enforces date sanity + Luhn, so bridging
/// can never manufacture a VALID false positive out of two unrelated numbers.</para>
///
/// <para>The persisted raw text is NEVER the normalized copy — only this transient
/// copy is scanned; segmentation and persistence use the original text.</para>
/// </summary>
public static partial class PersonnummerTextNormalizer
{
    // Bridge ONLY: an 8- or 6-digit run, then 1–2 Unicode space separators or tabs
    // (never a newline — a newline is a field/line boundary, not an OCR gap), then
    // EXACTLY 4 digits, with non-digit boundaries on both ends so we never bite into
    // a longer number. The gap is removed (digits joined) so the scanner's
    // no-separator alternative (\d{8}\d{4} / \d{6}\d{4}) then matches the joined token.
    //
    // #268 C1 (ADR 0074 Invariant 1): the gap class is the full Unicode space-separator
    // category \p{Zs} (plus tab), not just ASCII space. This product itself emits the
    // NON-BREAKING SPACE (U+00A0) as its Swedish digit-group separator (web format.ts),
    // and PDF/DOCX extraction passes U+00A0 / narrow-NBSP (U+202F) / thin/figure space
    // (U+2009/U+2007) through verbatim — so a personnummer written "19811218<NBSP>9876"
    // would otherwise NEVER be bridged, the scanner would miss it, and the import guard
    // would store it flagged as "no personnummer found" (a PII leak). \p{Zs} subsumes the
    // ASCII space (U+0020) so this only widens, never narrows, the prior bridge. The width
    // stays bounded at {1,2}: the defect is the character class, not the gap length, and a
    // wider window would needlessly raise the chance of bridging two unrelated numbers.
    // Safety is unchanged — Personnummer.TryParse's date+Luhn gate is still the only
    // authority, so widening candidate SHAPING can never manufacture a valid false positive.
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})[\p{Zs}\t]{1,2}(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedCandidateRegex();

    /// <summary>
    /// Returns a scan-copy of <paramref name="text"/> with personnummer-shaped
    /// space/OCR gaps bridged. Idempotent (a joined token has no gap left to bridge)
    /// and deterministic (single left-to-right pass, culture-invariant).
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return SpacedCandidateRegex().Replace(text, "$1$2");
    }
}
