using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Deterministic, pure-Domain scanner that detects Swedish personnummer and
/// samordningsnummer in free text (e.g. parsed CV text). No AI/LLM (ADR 0071).
///
/// Design intent (ADR 0074 Invariant 1): the scanner only ever FLAGS — it
/// returns the locations of personnummer so the user can be prompted to REMOVE
/// them. There is deliberately NO surface that adds, suggests, or echoes a
/// personnummer, and a <see cref="PersonnummerMatch"/> never carries the raw
/// value. A missed match is a PII leak (a false negative is strictly worse than
/// a rare over-flag), so candidate detection is permissive on shape while
/// <see cref="Personnummer.TryParse"/> enforces date sanity + Luhn.
/// </summary>
public static partial class PersonnummerScanner
{
    // Candidate token: a 10- or 12-digit personnummer-shaped run with an optional separator
    // before the final 4 digits — ASCII '-'/'+', any Unicode dash (\p{Pd}) or U+2212 MINUS
    // (#497), the same class the normalizer/TryParse paths admit. The flag path's
    // Normalize strips \p{Cf} globally before Scan, so this contiguous candidate needs no
    // in-group \p{Cf} tolerance (that lives only in GapAwareCandidateRegex). The lookbehind/lookahead reject
    // a candidate that is part of a longer digit run (token-boundary guard), so an
    // arbitrary 14-digit reference number that merely CONTAINS a valid personnummer
    // is never flagged. The 12-digit alternative is tried first so a full-century
    // number is matched whole rather than as its trailing 10 digits.
    [GeneratedRegex(@"(?<!\d)(?:\d{8}[-+\p{Pd}\u2212]?\d{4}|\d{6}[-+\p{Pd}\u2212]?\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    /// <summary>
    /// Scans <paramref name="text"/> for delimited personnummer/samordningsnummer
    /// tokens. Returns one <see cref="PersonnummerMatch"/> per detection, in
    /// source order. Never returns raw digits.
    /// </summary>
    public static IReadOnlyList<PersonnummerMatch> Scan(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        List<PersonnummerMatch>? matches = null;

        foreach (var candidate in CandidateRegex().EnumerateMatches(text))
        {
            var token = text.AsSpan(candidate.Index, candidate.Length);
            if (!Personnummer.TryParse(token, out var personnummer))
                continue;

            matches ??= [];
            matches.Add(PersonnummerMatch.Create(
                candidate.Index,
                candidate.Length,
                personnummer.Kind,
                personnummer.Masked));
        }

        return matches is null ? [] : matches;
    }

    // Gap-aware candidate for the REDACTION path (#427/#497/#498). The same personnummer shape
    // as CandidateRegex, but tolerant of a bridging gap between the leading 8-or-6 digit run and
    // the trailing 4 digits. The digit runs and the gap are composed of:
    //   * digit groups ((?:\d\p{Cf}*){8}/{6}/{4}) that tolerate an invisible zero-width format
    //     char (\p{Cf}) INTERLEAVED between digits (#498a): the import flag path strips \p{Cf}
    //     globally before Scan, so a \p{Cf} INSIDE a digit group is flagged there; the redaction
    //     path must mask it too, so \p{Cf}* rides after every digit, and/or
    //   * the GAP RULE, one rule for one piece of knowledge (the two-visible-column budget): a run of
    //     at most TWO separators, then up to TWO visible Unicode space separators or tabs, each
    //     followed by a run of at most TWO separators
    //     ((?:[SEP]\p{Cf}*){0,2}(?:[\p{Zs}\t]\p{Cf}*(?:[SEP]\p{Cf}*){0,2}){0,2}). The {0,2} on the
    //     visible columns is bounded in lockstep with the ExtractedDocumentText profile of
    //     PersonnummerTextNormalizer, NOT with that type as a whole, which since #1415 also
    //     carries a wider SingleLineUserInput profile that this path deliberately has no
    //     counterpart for. No Redact call site redacts a search query or any other
    //     single-line user input, so there is nothing here for the wider flag profile to be a
    //     superset of (ADR 0134 D7, pinned by PersonnummerGapProfileCallSiteTests). A 3+ visible-column
    //     gap therefore stays unbridged on this path, the accepted residual (#427 V3, ADR 0134 D1).
    //     SEP is ASCII '-'/'+', any Unicode dash (\p{Pd}), U+2212 MINUS (#497) or, since #2123,
    //     '.', '/' or '_'. The rule covers "811218- 9876" / "811218 -9876" (#427 R2), a dash-space-dash
    //     form (#498), "811218--9876" (#665), a Unicode-dash pnr (#497) and, since #2123, a separator
    //     BETWEEN the two columns ("811218 - 9876"), "811218.9876" as an employer types it into an ad,
    //     and two separators beside a column ("811218 -- 9876"). A line break is not a column, and it
    //     is not bridged (PersonnummerGapProfile.ExtractedDocumentText), and
    //   * any number of invisible zero-width format characters (\p{Cf}: U+200B, U+FEFF, ...)
    //     that PDF/DOCX extraction emits, freely INTERLEAVED anywhere in the gap (\p{Cf}* at
    //     each position), so "811218<sp><ZWSP><sp>9876" bridges even though the \p{Cf} breaks
    //     the visible-space run (#427 R1). Being invisible non-content they are unbounded; they
    //     do not count toward the {0,2} visible-column bound.
    // Unlike the import guard's Normalize→Scan path (which joins digits on a transient copy
    // and discards offsets), the redactor must mask IN PLACE, so this match must span the
    // ORIGINAL text INCLUDING the gap. The digit (\d), space (\p{Zs}\t), zero-width (\p{Cf})
    // and separator (ASCII '-'/'+', \p{Pd}, U+2212, '.', '/', '_') classes are pairwise DISJOINT
    // with bounded quantifiers and no alternation inside the gap, so the pattern is ReDoS-linear.
    // The '-'/'+'/\p{Pd}/U+2212 core of the separator class and the \p{Cf} class are shared with
    // PersonnummerTextNormalizer; '.', '/' and '_' are NOT: they are a deliberate widening of THIS
    // path only (#2123, ADR 0134 amendment). The flag path (Normalize→Scan, the CV import guard and
    // the ?q= box) is unchanged; the redactor also runs over CV file names, evidence and ATS text, and
    // masks the new forms there too. That is the safe direction of the superset invariant (the
    // redactor masks everything the flag path flags, plus these). The UNCHANGED
    // Personnummer.TryParse date+Luhn gate stays the ONLY authority, so a wider candidate SHAPE can
    // never manufacture a valid false positive out of two unrelated numbers;
    // PersonnummerBridgeCollisionRateTests pins that a new form collides exactly as often as the
    // same digits written contiguously.
    [GeneratedRegex(@"(?<!\d)(?:(?:\d\p{Cf}*){8}|(?:\d\p{Cf}*){6})(?:[-+\p{Pd}\u2212./_]\p{Cf}*){0,2}(?:[\p{Zs}\t]\p{Cf}*(?:[-+\p{Pd}\u2212./_]\p{Cf}*){0,2}){0,2}(?:\d\p{Cf}*){4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex GapAwareCandidateRegex();

    /// <summary>
    /// Gap-aware sibling of <see cref="Scan"/> for the REDACTION path
    /// (<see cref="PersonnummerRedactor"/>). Detects the same personnummer/
    /// samordningsnummer shapes PLUS spaced/OCR-gapped, zero-width-gapped, Unicode-dash (#497),
    /// zero-width-inside-a-digit-group / dash-space-dash (#498) and, since #2123, space-separator-space
    /// ("811218 - 9876") and '.' / '/' / '_' separated forms, returning each
    /// match's offset + length into the ORIGINAL
    /// <paramref name="text"/> — the gap characters are part of the span — so a redactor
    /// can mask the raw digits IN PLACE without any offset translation. Each match's
    /// <see cref="PersonnummerMatch.Masked"/> keeps every non-digit (separator + bridging
    /// gap) and preserves length, so it maps 1:1 onto the original span. Detection is
    /// gated by the UNCHANGED <see cref="Personnummer.TryParse"/> (date sanity + Luhn),
    /// so the wider candidate shape never over-flags. Never returns raw digits.
    /// </summary>
    public static IReadOnlyList<PersonnummerMatch> ScanWithGaps(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        List<PersonnummerMatch>? matches = null;

        // The stripped token is DIGIT-ONLY (#497/#498, Approach A): every non-digit — a
        // separator or a bridging gap character — is dropped, so the
        // token is the joined digit sequence the shared TryParse validates, exactly mirroring
        // the import flag path's Normalize ($1$2 digit-join). Each kept digit is the Unicode
        // decimal-digit category \p{Nd} FOLDED to its ASCII 0-9 value (#667), the same fold
        // TryParse applies, so a fullwidth (U+FF10-U+FF19) personnummer validates identically on
        // both paths and its ORIGINAL span still masks in place (MaskSpan masks every \p{Nd}).
        // No personnummer shape yields more
        // than 12 digits (the 12-digit full-century form), so char[14] over-provisions and the
        // overflow guard below is pure defense-in-depth against a later regex change (never hit
        // today). Masking still uses the ORIGINAL span (gap + separators kept), not this token.
        Span<char> token = stackalloc char[14];

        // A candidate TryParse rejects must not hide a real personnummer that starts INSIDE it. The
        // candidate shape is permissive on purpose and TryParse is the only authority, but a plain
        // non-overlapping enumeration lets a rejected candidate consume text: "556677.8112<ZWSP>18-9876"
        // yields the candidate "556677.8112" (rejected), and the personnummer that begins at "8112" was
        // skipped although the flag path, which strips the zero-width character first, flags it
        // (flagged-but-unmasked, the break #465/#498 closed). So after a rejection scanning resumes one
        // position later; an ACCEPTED match still consumes its span. The lookbehind still forbids a
        // start in the middle of a digit run, so the extra work is bounded by the candidate length.
        var start = 0;
        while (start < text.Length)
        {
            var candidates = GapAwareCandidateRegex().EnumerateMatches(text, start);
            if (!candidates.MoveNext())
                break;

            var candidate = candidates.Current;
            var span = text.AsSpan(candidate.Index, candidate.Length);

            // Build the DIGIT-ONLY token (#497/#498, Approach A): keep every decimal digit
            // (\p{Nd}) FOLDED to its ASCII 0-9 value (#667 — fullwidth U+FF10-U+FF19 and other
            // Nd forms), drop every separator and every bridging gap char
            // (\p{Zs}/\t/\p{Cf}). This mirrors the flag path's $1$2 digit-join AND TryParse's own
            // Nd-fold, so a dash-space-dash form ("811218- -9876", #498b), a Unicode-dash form
            // (#497) and a fullwidth-digit form (#667) validate identically on both paths (no new
            // divergence). The ORIGINAL span (gap included) is what we hand to the match for
            // in-place masking. Bail defensively if a span ever carries more digits than any
            // personnummer shape can — never reached today (max 12); future-proofs the buffer.
            var length = 0;
            var overflowed = false;
            foreach (var c in span)
            {
                var digitValue = CharUnicodeInfo.GetDecimalDigitValue(c);
                if (digitValue >= 0)
                {
                    if (length == token.Length)
                    {
                        overflowed = true;
                        break;
                    }

                    token[length++] = (char)('0' + digitValue);
                }
            }

            if (overflowed || !Personnummer.TryParse(token[..length], out var personnummer))
            {
                start = candidate.Index + 1;
                continue;
            }

            matches ??= [];
            matches.Add(PersonnummerMatch.Create(
                candidate.Index,
                candidate.Length,
                personnummer.Kind,
                Personnummer.MaskSpan(span)));
            start = candidate.Index + candidate.Length;
        }

        return matches is null ? [] : matches;
    }
}
