using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// Reduces a PdfPig font name to a comparable family token for the D3 allowlist match (Fas 4b
/// #891, ADR 0108). This is parser FORM — the mechanics of PDF font naming, which do NOT vary
/// with Swedish CV convention or ATS policy — so it lives in C#, NOT as knowledge-bank data
/// (§5 targets domain lists like the font allowlist itself, which is why THAT is in
/// <c>cv-conventions</c>). The SAME normaliser runs over both the observed font names and the
/// allowlist entries, and the D3 rule compares the results for <b>exact equality</b>: an unknown
/// or unresolvable name yields a token that matches nothing → the rule Warns (never a fabricated
/// allowlist match → Pass). "When in doubt, do not match" is the §5-honest failure mode.
/// </summary>
internal static partial class FontNameNormalizer
{
    // Leading 6-uppercase-letter subset tag ("ABCDEF+Arial") — PDF font subsetting.
    [GeneratedRegex(@"^[A-Z]{6}\+", RegexOptions.CultureInvariant)]
    private static partial Regex SubsetTag();

    // Split before an uppercase that follows a lower-case/digit (camelCase), and before an
    // uppercase that begins a word after an all-caps acronym ("RomanPSMT" → "Roman", "PSMT").
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundary();

    // Closed set of style/weight/tech tokens dropped from the family (case-insensitive). NOT a
    // §5 data list — these are intrinsic PDF font-name mechanics (a weight is a weight in every
    // language). Weight PREFIXES (Semi/Demi/Extra/Ultra) are listed because camelCase splitting
    // turns "SemiBold" into "Semi"+"Bold". "Roman" is DELIBERATELY absent — it is part of the
    // "Times New Roman" family, not a style. Dropping errs toward a non-match (Warn), never a Fail.
    private static readonly HashSet<string> StyleAndTechTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bold", "Italic", "Oblique", "Light", "Medium", "SemiBold", "Semi", "Demi", "Extra",
        "Ultra", "Regular", "Thin", "Black", "Book", "Heavy", "MT", "PS", "PSMT",
    };

    private static readonly char[] Separators = ['-', ',', ' ', '\t'];

    /// <summary>
    /// Normalises a raw font name to its comparable family token (e.g. "ABCDEF+Arial-BoldMT" →
    /// "arial", "TimesNewRomanPSMT" → "timesnewroman", "ArialNarrow" → "arialnarrow" — Narrow is a
    /// width, not a style, so it stays and correctly does NOT match "arial"). Returns the empty
    /// string for a null/blank name or one that reduces to nothing (both unresolvable → no match).
    /// </summary>
    internal static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var stripped = SubsetTag().Replace(raw, string.Empty);
        var separated = CamelBoundary().Replace(stripped.Replace('-', ' ').Replace(',', ' '), " ");

        var family = separated
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !StyleAndTechTokens.Contains(token));

        return string.Concat(family).ToLowerInvariant();
    }
}
