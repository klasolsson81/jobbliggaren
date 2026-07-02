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
    // (#497), the same class the redaction/normalizer/TryParse paths admit. The flag path's
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
    //   * up to TWO visible Unicode space separators or tabs ((?:[\p{Zs}\t]\p{Cf}*){0,2},
    //     bounded {0,2} EXACTLY like PersonnummerTextNormalizer's {1,2} — a 3+ visible-column
    //     gap is deliberately NOT bridged; a wider window would raise the chance of bridging
    //     two unrelated numbers, the reviewed accepted residual #427 V3), and/or
    //   * at most ONE separator per side, ADJACENT to the space run on either side
    //     ((?:[SEP]\p{Cf}*)? before AND after) where SEP is ASCII '-'/'+', any Unicode dash
    //     (\p{Pd}) or U+2212 MINUS (#497): a realistic OCR/Word rendering of a legitimate
    //     separator, e.g. "811218- 9876" / "811218 -9876" (#427 R2) / a Unicode-dash pnr (#497), and/or
    //   * any number of invisible zero-width format characters (\p{Cf}: U+200B, U+FEFF, ...)
    //     that PDF/DOCX extraction emits, freely INTERLEAVED anywhere in the gap (\p{Cf}* at
    //     each position) — so "811218<sp><ZWSP><sp>9876" bridges even though the \p{Cf} breaks
    //     the visible-space run (#427 R1). Being invisible non-content they are unbounded; they
    //     do not count toward the {0,2} visible-column bound.
    // Unlike the import guard's Normalize→Scan path (which joins digits on a transient copy
    // and discards offsets), the redactor must mask IN PLACE, so this match must span the
    // ORIGINAL text INCLUDING the gap. The digit (\d), space (\p{Zs}\t), zero-width (\p{Cf})
    // and separator (ASCII '-'/'+', \p{Pd}, U+2212) classes are pairwise DISJOINT with bounded
    // quantifiers, so the pattern is ReDoS-linear. The separator + \p{Cf} class is shared with
    // PersonnummerTextNormalizer AND Personnummer.TryParse; the UNCHANGED Personnummer.TryParse
    // date+Luhn gate stays the ONLY authority, so a wider candidate SHAPE can never manufacture
    // a valid false positive out of two unrelated numbers.
    [GeneratedRegex(@"(?<!\d)(?:(?:\d\p{Cf}*){8}|(?:\d\p{Cf}*){6})(?:[-+\p{Pd}\u2212]\p{Cf}*)?(?:[\p{Zs}\t]\p{Cf}*){0,2}(?:[-+\p{Pd}\u2212]\p{Cf}*)?(?:\d\p{Cf}*){4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex GapAwareCandidateRegex();

    /// <summary>
    /// Gap-aware sibling of <see cref="Scan"/> for the REDACTION path
    /// (<see cref="PersonnummerRedactor"/>). Detects the same personnummer/
    /// samordningsnummer shapes PLUS spaced/OCR-gapped, zero-width-gapped, Unicode-dash (#497)
    /// and zero-width-inside-a-digit-group / dash-space-dash (#498) forms, returning each
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

        // The stripped token is DIGIT-ONLY (#497/#498, Approach A): every non-digit — ASCII
        // '-'/'+', a Unicode dash, and the bridging \p{Zs}/\t/\p{Cf} gap — is dropped, so the
        // token is the joined digit sequence the UNCHANGED TryParse validates, exactly mirroring
        // the import flag path's Normalize ($1$2 digit-join). No personnummer shape yields more
        // than 12 digits (the 12-digit full-century form), so char[14] over-provisions and the
        // overflow guard below is pure defense-in-depth against a later regex change (never hit
        // today). Masking still uses the ORIGINAL span (gap + separators kept), not this token.
        Span<char> token = stackalloc char[14];

        foreach (var candidate in GapAwareCandidateRegex().EnumerateMatches(text))
        {
            var span = text.AsSpan(candidate.Index, candidate.Length);

            // Build the DIGIT-ONLY token (#497/#498, Approach A): keep ASCII digits, drop every
            // separator (ASCII/Unicode dash) and every bridging gap char (\p{Zs}/\t/\p{Cf}). This
            // mirrors the flag path's $1$2 digit-join, so a dash-space-dash form ("811218- -9876",
            // #498b) and a Unicode-dash form (#497) validate identically on both paths (no new
            // divergence). The ORIGINAL span (gap included) is what we hand to the match for
            // in-place masking. Bail defensively if a span ever carries more digits than any
            // personnummer shape can — never reached today (max 12); future-proofs the buffer.
            var length = 0;
            var overflowed = false;
            foreach (var c in span)
            {
                if (char.IsAsciiDigit(c))
                {
                    if (length == token.Length)
                    {
                        overflowed = true;
                        break;
                    }

                    token[length++] = c;
                }
            }

            if (overflowed || !Personnummer.TryParse(token[..length], out var personnummer))
                continue;

            matches ??= [];
            matches.Add(PersonnummerMatch.Create(
                candidate.Index,
                candidate.Length,
                personnummer.Kind,
                Personnummer.MaskSpan(span)));
        }

        return matches is null ? [] : matches;
    }
}
