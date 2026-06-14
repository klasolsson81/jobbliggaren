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
    // Candidate token: a 10- or 12-digit personnummer-shaped run with an optional
    // '-'/'+' separator before the final 4 digits. The lookbehind/lookahead reject
    // a candidate that is part of a longer digit run (token-boundary guard), so an
    // arbitrary 14-digit reference number that merely CONTAINS a valid personnummer
    // is never flagged. The 12-digit alternative is tried first so a full-century
    // number is matched whole rather than as its trailing 10 digits.
    [GeneratedRegex(@"(?<!\d)(?:\d{8}[-+]?\d{4}|\d{6}[-+]?\d{4})(?!\d)", RegexOptions.CultureInvariant)]
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
}
