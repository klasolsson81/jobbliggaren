namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// LIKE patterns over user text. The metacharacters (<c>%</c>, <c>_</c> and the escape character
/// itself) are escaped so a term like "50%" matches literally rather than as a wildcard; <c>\</c> is
/// PostgreSQL's default LIKE escape, so the two-argument <c>EF.Functions.Like</c> needs no ESCAPE clause.
/// </summary>
internal static class LikePatterns
{
    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>A lower-cased "contains" pattern, for a <c>lower(column) LIKE</c> predicate.</summary>
    public static string Contains(string term) =>
        $"%{Escape(term.ToLowerInvariant())}%";
}
