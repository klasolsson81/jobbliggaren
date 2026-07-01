using System.Text.RegularExpressions;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Produces a transient SCAN-COPY of free text in which a narrow,
/// personnummer-shaped digit–gap–digit pattern has its bridging whitespace removed
/// (and any invisible zero-width <c>\p{Cf}</c> character stripped, #427 V2), so the
/// context-free <see cref="PersonnummerScanner"/> can FLAG spaced/OCR-gapped and
/// zero-width-gapped forms (e.g. <c>19811218 9876</c>, <c>811218 9876</c>). This is the
/// F4-8 call-site fix for the spaced-form false-negative F4-1 deferred here (ADR 0074
/// Invariant 1).
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
    // A 3+ visible-column gap is therefore deliberately NOT bridged — a reviewed, accepted
    // residual (#427 V3, senior-cto-advisor). This bound governs only the VISIBLE \p{Zs}\t
    // separators; invisible zero-width \p{Cf} noise is handled separately below (stripped,
    // unbounded), so the {1,2} bound is unaffected by that widening.
    // Safety is unchanged — Personnummer.TryParse's date+Luhn gate is still the only
    // authority, so widening candidate SHAPING can never manufacture a valid false positive.
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})[\p{Zs}\t]{1,2}(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedCandidateRegex();

    // #427 V2 (ADR 0074 Invariant 1): zero-width FORMAT characters (\p{Cf} — U+200B
    // ZERO WIDTH SPACE, U+FEFF ZERO WIDTH NO-BREAK SPACE, U+200C/D, ...) are NOT in the
    // \p{Zs} space-separator class, so a personnummer gapped by one (e.g. "19811218<ZWSP>9876"
    // — a shape PDF/DOCX extraction emits) would otherwise slip past the bridge and be a
    // false negative. Being invisible non-content, they are STRIPPED entirely from this
    // transient scan-copy (never from persisted text — see the class docstring) BEFORE the
    // \p{Zs}\t bridge runs, so the joined digits are then matched. Stripping is unbounded
    // (a zero-width char is noise, not a gap whose width matters) and safe on a copy the
    // UNCHANGED TryParse date+Luhn gate still governs.
    [GeneratedRegex(@"\p{Cf}", RegexOptions.CultureInvariant)]
    private static partial Regex ZeroWidthFormatRegex();

    /// <summary>
    /// Returns a scan-copy of <paramref name="text"/> with personnummer-shaped
    /// space/OCR gaps bridged. Zero-width format characters (\p{Cf}) are stripped first
    /// (#427 V2) so a zero-width-gapped personnummer is bridged too. Idempotent (a joined
    /// token has no gap left to bridge, and a stripped copy has no zero-width char left)
    /// and deterministic (single left-to-right pass, culture-invariant).
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Strip invisible zero-width noise first (#427 V2), then bridge the visible
        // \p{Zs}\t gap. Order matters: a "digits<ZWSP><NBSP>digits" form is only bridged
        // once the zero-width char no longer sits inside the {1,2} space window.
        var stripped = ZeroWidthFormatRegex().Replace(text, string.Empty);
        return SpacedCandidateRegex().Replace(stripped, "$1$2");
    }
}
