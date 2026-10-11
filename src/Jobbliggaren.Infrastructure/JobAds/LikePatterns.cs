using System.Text;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// LIKE patterns over user text. The metacharacters (<c>%</c>, <c>_</c> and the escape character
/// itself) are escaped so a term like "50%" matches literally rather than as a wildcard. The pattern must
/// be paired with <see cref="EscapeCharacter"/>: EF Npgsql emits <c>ESCAPE ''</c> for the two-argument
/// <c>EF.Functions.Like</c>, which turns the escape off.
/// </summary>
internal static class LikePatterns
{
    public const string EscapeCharacter = "\\";

    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>A lower-cased "contains" pattern, for a <c>lower(column) LIKE</c> predicate.</summary>
    public static string Contains(string term) =>
        $"%{Escape(term.ToLowerInvariant())}%";

    /// <summary>
    /// True when the term holds a run of at least three letters or digits. pg_trgm extracts its
    /// trigrams from such runs, so a "contains" pattern without one ("!!!", "c++", "ab") cannot be
    /// served by a trigram index and scans all of it.
    /// </summary>
    public static bool HasIndexableTrigram(string term)
    {
        var run = 0;
        foreach (var rune in term.EnumerateRunes())
        {
            run = Rune.IsLetterOrDigit(rune) ? run + 1 : 0;
            if (run >= 3)
                return true;
        }

        return false;
    }
}
