using System.Text.RegularExpressions;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Produces a transient SCAN-COPY of free text in which a narrow,
/// personnummer-shaped digit–gap–digit pattern has its bridging whitespace removed
/// (and any invisible zero-width <c>\p{Cf}</c> character stripped, #427 V2), so the
/// context-free <see cref="PersonnummerScanner"/> can FLAG spaced/OCR-gapped and
/// zero-width-gapped forms (e.g. <c>19811218 9876</c>, <c>811218 9876</c>) — and, since #665,
/// the two-separator no-space form (<c>811218--9876</c>) the redaction path already masks. This is the
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
    // Bridge ONLY: an 8- or 6-digit run, then 0–2 Unicode space separators or tabs
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
    // stays bounded at {0,2} ON THIS PROFILE, and #1415 re-adjudicated that bound rather than
    // letting it be inherited: a 3+ visible-column gap is still deliberately NOT bridged in
    // extracted document text (#427 V3, re-affirmed by senior-cto-advisor in ADR 0134 — now on
    // MEASURED ground, see PersonnummerGapProfile). This bound governs only the VISIBLE
    // \p{Zs}\t separators; invisible zero-width \p{Cf} noise is handled separately below
    // (stripped, unbounded), so the {0,2} bound is unaffected by that widening.
    //
    // #427 (2nd CTO ruling, R2) + #497: an optional separator is now tolerated ADJACENT to the
    // {0,2} space run on either side ((?:[-+\p{Pd}...])? before AND after), where the separator
    // class is ASCII '-'/'+', any Unicode dash (\p{Pd}) or U+2212 MINUS (#497 — Word/PDF emit
    // these), so a realistic rendering of a legitimate separator — "811218- 9876" / "811218 -9876"
    // / a Unicode-dash spaced form — is bridged too. #665
    // (STEG 1 hardening): the visible-space run is {0,2}, NOT {1,2}, so the TWO-separator
    // ZERO-space form "811218--9876" is bridged too — the redaction path
    // (GapAwareCandidateRegex, sep? space{0,2} sep?) already masks it, but a MANDATORY space
    // meant the flag path could never reach it (a redaction-superset-of-flag false negative).
    // {0,2} gives this normalizer STRUCTURAL PARITY with GapAwareCandidateRegex (modulo \p{Cf},
    // stripped globally first), closing the divergence at its root. The degenerate all-empty
    // case (a pure contiguous "8112189876" / a single-separator "811218-9876" — no space) now
    // matches too, but joins to itself / drops one separator: an idempotent no-op the Scan path
    // already flags directly, so it adds neither detection nor over-flag. The replacement joins only the
    // two digit groups ($1$2), dropping the separator/space, so the joined token stays a valid Scan
    // candidate. Safety is unchanged — Personnummer.TryParse's date+Luhn gate is still the only
    // authority, so widening candidate SHAPING can never manufacture a valid false positive. This
    // separator class is shared with PersonnummerScanner and Personnummer.TryParse (symmetry).
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})(?:[-+\p{Pd}\u2212])?[\p{Zs}\t]{0,2}(?:[-+\p{Pd}\u2212])?(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedCandidateRegex();

    // #1415 / ADR 0134 — the SingleLineUserInput profile. Same grammar as above, two
    // deliberate differences, and each is keyed to a property of the input rather than to a
    // wish for more detection:
    //
    // 1. GAP CLASS [\s\p{Cc}]. Written as ONE character class, never an alternation: the
    //    ReDoS linearity argument below rests on the alternatives being pairwise disjoint,
    //    and \s overlaps \p{Cc} on \t\n\r\v\f, so an alternation would make that argument
    //    quietly false. .NET's \s is [\p{Zs}\p{Zl}\p{Zp}\f\n\r\t\v\x85] — note \p{Zl}, which
    //    is what carries U+2028 LINE SEPARATOR. Neither \p{Zs} nor \p{Cc} contains it, so the
    //    otherwise-natural [\p{Zs}\p{Cc}] would have left U+2028 as an UNDOCUMENTED residual
    //    (measured 2026-08-20 against the shipped chain, alongside U+000B and U+000C).
    //
    // 2. BOUND {0,8}. A line break is a field boundary in extracted document text and the
    //    bound there exists to keep a bridge from crossing one. A hand-typed search box has
    //    no line structure for a break to bound: whatever separates the digit runs is
    //    stuffing, not layout. The number is a risk level rather than a corpus size — {0,4}
    //    would cover every vector measured on this issue and still miss a five-space gap,
    //    which is fitting the bound to the test set instead of to the threat.
    //
    // \p{Cf} is NOT folded into this class. It is stripped globally below, and it must stay
    // stripped rather than bridged: treating it as a gap character would re-open the
    // "811218 9876<U+0001>5" counterexample the #1414 measurement turned on.
    [GeneratedRegex(@"(?<!\d)(\d{8}|\d{6})(?:[-+\p{Pd}\u2212])?[\s\p{Cc}]{0,8}(?:[-+\p{Pd}\u2212])?(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SingleLineGapCandidateRegex();

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
    /// space/OCR gaps bridged under <paramref name="profile"/>. Zero-width format characters
    /// (\p{Cf}) are stripped first (#427 V2) so a zero-width-gapped personnummer is bridged
    /// too. Idempotent (a joined token has no gap left to bridge, and a stripped copy has no
    /// zero-width char left) and deterministic (single left-to-right pass, culture-invariant).
    ///
    /// <para>The profile is REQUIRED and has no default (#1415, ADR 0134). The two policies
    /// are not orderable by strength — the wider one is unsafe on extracted document text,
    /// where a bridged line break collides at ~1 in 10, and the narrower one is unsafe on a
    /// hand-typed box, where it misses thirteen gap classes that persist in plaintext and
    /// render verbatim. A default would silently pick one of those wrongs for whichever call
    /// site was written next.</para>
    /// </summary>
    public static string Normalize(string text, PersonnummerGapProfile profile)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Strip invisible zero-width noise first (#427 V2), then bridge the profile's gap.
        // Order matters: a "digits<ZWSP><NBSP>digits" form is only bridged once the
        // zero-width char no longer sits inside the space window.
        var stripped = ZeroWidthFormatRegex().Replace(text, string.Empty);
        var bridge = profile switch
        {
            PersonnummerGapProfile.SingleLineUserInput => SingleLineGapCandidateRegex(),
            _ => SpacedCandidateRegex(),
        };
        return bridge.Replace(stripped, "$1$2");
    }
}
