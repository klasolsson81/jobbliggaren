using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic CV date-token patterns shared across parsing (F4-8) and the review engine
/// (F4-9). Promoted to this neutral <c>Infrastructure/Resumes/Parsing</c> home so the ONE
/// knowledge piece — "what a CV date range / bare year looks like" — has a single owner
/// (DRY, CLAUDE.md §9.1; parity with <see cref="PeriodParser"/>, promoted here for the same
/// reason). <see cref="HeadingDrivenResumeSegmenter"/> matches these to extract/strip a
/// period from an entry; <see cref="Review.Rules.ReviewText"/> masks them so an employment
/// date is never miscounted as a measurable result (#487). The patterns are word-bounded
/// (mid-text), NOT anchored — contrast <see cref="PeriodParser"/>, which anchors <c>^…$</c>
/// for whole-string parsing.
/// </summary>
internal static partial class DatePatterns
{
    // A date RANGE: a start point (YYYY, MM/YYYY or ISO YYYY-MM) — dash — an end point or a
    // present-keyword. (Kept byte-identical to the pattern the segmenter previously owned so
    // extract/strip behaviour is unchanged by the promotion.)
    [GeneratedRegex(
        @"\b(\d{4}|\d{2}/\d{4}|\d{4}-\d{2})\s*[-–—]\s*(\d{4}|\d{2}/\d{4}|\d{4}-\d{2}|nuvarande|pågående|pagaende|present|current|now|idag|nu)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex DateRange();

    // A bare four-digit year 1900–2099.
    [GeneratedRegex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    public static partial Regex Year();

    /// <summary>
    /// Replaces every date range and bare year in <paramref name="text"/> with a space, so a
    /// downstream digit test cannot mistake an employment date for a quantified result (#487).
    /// Ranges are masked before bare years so a range's inner years are consumed with the range.
    /// </summary>
    public static string StripDates(string text) =>
        Year().Replace(DateRange().Replace(text, " "), " ");
}
