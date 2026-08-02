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

    /// <summary>
    /// Removes a TRAILING date range or bare year from <paramref name="line"/>, together with the
    /// separators left dangling behind it. Only strips when the match runs to the END of the line:
    /// a leading or internal year is likely part of a name ("Studio 2005 Design") and is left alone.
    /// Returns <paramref name="line"/> unchanged when no match reaches the end.
    ///
    /// <para>Promoted here from <see cref="HeadingDrivenResumeSegmenter"/> (#1060 β-3 follow-up) so
    /// that <see cref="IsDateOnlyLine"/> can be defined AS this reduction rather than as a second
    /// copy of it. Behaviour is byte-identical to the segmenter's former private method.</para>
    /// </summary>
    public static string StripTrailingDate(string line)
    {
        var range = DateRange().Match(line);
        if (range.Success && line[(range.Index + range.Length)..].Trim().Length == 0)
            return TrimTrailingSeparators(line[..range.Index]);

        var year = Year().Match(line);
        if (year.Success && line[(year.Index + year.Length)..].Trim().Length == 0)
            return TrimTrailingSeparators(line[..year.Index]);

        return line;
    }

    /// <summary>
    /// True when a date match consumes the WHOLE of <paramref name="line"/>, modulo surrounding
    /// separators — "the line carries a date and nothing else". This is
    /// <see cref="StripTrailingDate"/> asked as a question, deliberately not a second
    /// implementation: one knowledge piece, one home (DRY, CLAUDE.md §9.1), which is the same move
    /// that gave <see cref="DatePatterns"/> and <see cref="PeriodParser"/> their neutral home.
    ///
    /// <para><b>Two readers, and they must agree.</b> The segmenter asks it to refuse a field —
    /// a line carrying no field must not BECOME one (#1060 β-3) — and
    /// <see cref="Review.Rules.ReviewText.DescriptionLines"/> asks it to refuse a bullet, so the
    /// review engine never scores the user's date row as prose. Before the promotion those two
    /// agreed only by accident: the review side suppressed such a line via its
    /// organization-equality test, which fired only BECAUSE the segmenter had fabricated the line
    /// into the organization slot. β-3 stopped that fabrication and the accident with it.</para>
    ///
    /// <para><b>It does not subsume <see cref="PeriodParser"/>, and must never replace it.</b>
    /// `PeriodParser` is WIDER on FOUR measured axes — the word separators "till"/"to";
    /// single-digit months (<c>\d{1,2}</c> against this type's <c>\d{2}</c>); "." / "-" as month
    /// separators where this type takes only "/"; and ISO <c>YYYY-MM</c> END points, because
    /// <see cref="DateRange"/>'s end-alternation is ordered so the bare <c>\d{4}</c> matches first
    /// and the word boundary after it holds against the following "-", leaving a non-empty tail.
    /// So "2019 till 2021", "3/2020 – 6/2024" and "2020-06 – 2024-03" are all periods this
    /// predicate declines. Where both are available the callers take their UNION; substituting one
    /// for the other narrows suppression in the opposite direction. This predicate is wider only
    /// for a line whose date is not a whole-string period — a LEADING separator
    /// ("– 2020 – 2024"), which `PeriodParser` is anchored against.</para>
    /// </summary>
    public static bool IsDateOnlyLine(string line) => StripTrailingDate(line).Length == 0;

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(' ', '\t', ',', ';', '|', '-', '–', '—');
}
