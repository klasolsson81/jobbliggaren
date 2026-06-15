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
    // Bridge ONLY: an 8- or 6-digit run, then 1–2 spaces/tabs (never a newline — a
    // newline is a field/line boundary, not an OCR gap), then EXACTLY 4 digits, with
    // non-digit boundaries on both ends so we never bite into a longer number. The
    // gap is removed (digits joined) so the scanner's no-separator alternative
    // (\d{8}\d{4} / \d{6}\d{4}) then matches the joined token.
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})[ \t]{1,2}(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
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
