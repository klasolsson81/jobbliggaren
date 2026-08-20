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
    // The grammar is single-sourced: the two profiles below differ in the GAP TERM and in
    // nothing else, and that is now structural rather than a convention anyone has to keep.
    // ADR 0134 owns the policy; this block owns the shape.
    private const string Lead = @"(?<!\d)(\d{8}|\d{6})";
    // This class is duplicated verbatim in PersonnummerScanner's two GeneratedRegex patterns
    // (three homes for one literal) and must stay in lockstep with them.
    //
    // The two directions are NOT equally guarded, and the unguarded one is the dangerous one.
    // NARROWING is caught: dropping \p{Pd} fails four Domain tests, the equivalence suite among
    // them. WIDENING is not: adding a character outside the equivalence corpus' closed
    // separator repertoire leaves the whole Domain suite green, because that corpus only ever
    // generates the nine separators it already lists. So a widening here — which lets the flag
    // path bridge a form the unchanged redaction path cannot mask — reaches
    // flagged-but-unmasked with nothing failing. Widen the redaction path in the same commit,
    // or do not widen this.
    private const string Sep = @"(?:[-+\p{Pd}\u2212])?";
    private const string Tail = @"(\d{4})(?!\d)";

    // Bridge ONLY: an 8- or 6-digit run, then the profile's gap, then EXACTLY 4 digits, with
    // non-digit boundaries on both ends so we never bite into a longer number. The gap is
    // removed (digits joined) so the scanner's no-separator alternative then matches.
    //
    // #268 C1 (ADR 0074 Invariant 1): the gap class is the full \p{Zs} category plus tab, not
    // just ASCII space. This product itself emits U+00A0 as its Swedish digit-group separator,
    // and PDF/DOCX extraction passes U+00A0 / U+202F / U+2009 / U+2007 through verbatim.
    // #427 R2 + #497: one optional separator ADJACENT to the gap run on either side, the class
    // being ASCII '-'/'+', any \p{Pd}, or U+2212. #665: the run is {0,2} and not {1,2}, so the
    // zero-space two-separator form "811218--9876" is bridged — a MANDATORY space had made the
    // flag path unable to reach a form the redaction path already masked.
    //
    // The {0,2} bound and the newline exclusion are the ExtractedDocumentText policy, and #1415
    // re-adjudicated them rather than letting them be inherited. ADR 0134 carries the ground and
    // PersonnummerBridgeCollisionRateTests regenerates the numbers behind it.
    [GeneratedRegex(Lead + Sep + @"[\p{Zs}\t]{0,2}" + Sep + Tail, RegexOptions.CultureInvariant)]
    private static partial Regex SpacedCandidateRegex();

    // #1415 / ADR 0134 — the SingleLineUserInput profile. Two deliberate differences from the
    // gap term above, each keyed to a property of the input rather than to a wish for more
    // detection:
    //
    // 1. GAP CLASS [\s\p{Cc}], written as ONE character class and never an alternation: the
    //    ReDoS linearity argument in PersonnummerScanner rests on the alternatives being
    //    pairwise disjoint, and \s overlaps \p{Cc} on \t\n\r\v\f, so an alternation would make
    //    that argument quietly false. .NET's \s is [\p{Zs}\p{Zl}\p{Zp}\f\n\r\t\v\x85] — note
    //    \p{Zl}, which is what carries U+2028 LINE SEPARATOR. Neither \p{Zs} nor \p{Cc}
    //    contains it, so the otherwise-natural [\p{Zs}\p{Cc}] would have left U+2028 an
    //    UNDOCUMENTED residual.
    //
    // 2. BOUND {0,8}. A line break is a field boundary in extracted document text and the bound
    //    there exists to keep a bridge from crossing one. A hand-typed box has no line structure
    //    for a break to bound: whatever separates the digit runs is stuffing, not layout. The
    //    number is a risk level, not a corpus size. What sits ABOVE it is a declared residual —
    //    ADR 0134 R3, pinned by Normalize_SingleLineUserInput_BoundIsEightNotUnbounded.
    //
    // \p{Cc} must never move to the \p{Cf} strip below. Stripping it would glue the following
    // character onto the candidate, so "811218 9876<U+0001>5" would become "811218 98765" and
    // the trailing digit would defeat Tail's (?!\d). The load-bearing property there is
    // NON-MEMBERSHIP IN THE STRIP CLASS, not membership in this gap class: the match is
    // "811218 9876" and the control character sits OUTSIDE it, RETAINED as the non-digit
    // boundary that satisfies (?!\d). What earns \p{Cc} its place in the gap class is the
    // separate case of a personnummer gapped BY a control character, which the theory's
    // U+0001 and space-Cc-space rows pin.
    [GeneratedRegex(Lead + Sep + @"[\s\p{Cc}]{0,8}" + Sep + Tail, RegexOptions.CultureInvariant)]
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
    /// where a bridged line break collides far more often than the F4-8 estimate assumed
    /// (PersonnummerBridgeCollisionRateTests regenerates the rates), and the
    /// narrower one is unsafe on a hand-typed box, where it misses twelve gap classes that
    /// persist in plaintext and render verbatim. A default would silently pick one of those
    /// wrongs for whichever call site was written next.</para>
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
        // Exhaustive, and the catch-all THROWS rather than falling back. A silent fallback here
        // would reinstate one layer down exactly the default this method's signature removes —
        // and it would fall to the narrower policy, i.e. fewer detections, which is the wrong
        // direction for a PII guard. Same shape as DomainError.ToProblemResult()'s `_`.
        var bridge = profile switch
        {
            PersonnummerGapProfile.SingleLineUserInput => SingleLineGapCandidateRegex(),
            PersonnummerGapProfile.ExtractedDocumentText => SpacedCandidateRegex(),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };
        return bridge.Replace(stripped, "$1$2");
    }
}
