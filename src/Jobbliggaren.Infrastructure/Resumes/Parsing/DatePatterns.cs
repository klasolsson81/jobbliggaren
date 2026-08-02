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
    /// True when a date match runs to the END of <paramref name="line"/> and nothing but separators
    /// precedes it — "the line carries a date and nothing else". <b>Whitespace after the match is
    /// tolerated; any other trailing glyph is not</b> — the trim runs on the remainder to the LEFT
    /// of the match, never on the tail, so <c>"2005 - 2010,"</c> is false. Also true VACUOUSLY for
    /// the empty line, which carries no match at all — declared unreachable from every call site
    /// and pinned as such, not a claim about what production does with one. This is
    /// <see cref="StripTrailingDate"/> asked as a question, deliberately not a second
    /// implementation: one knowledge piece, one home (DRY, CLAUDE.md §9.1), which is the same move
    /// that gave <see cref="DatePatterns"/> and <see cref="PeriodParser"/> their neutral home.
    ///
    /// <para><b>Two readers, and they must agree</b> — three call sites across them: the segmenter
    /// reads the reduction in <c>StripTrailingPeriod</c> and this predicate directly in
    /// <c>SplitTitleOrganization</c>. The segmenter asks it to refuse a field —
    /// a line carrying no field must not BECOME one (#1060 β-3) — and
    /// <see cref="Review.Rules.ReviewText.DescriptionLines"/> asks it to refuse a bullet, so the
    /// review engine never scores the user's date row as prose. Before the promotion those two
    /// agreed only by accident: the review side suppressed such a line via its
    /// organization-equality test, which fired only BECAUSE the segmenter had fabricated the line
    /// into the organization slot. β-3 stopped that fabrication and the accident with it.</para>
    ///
    /// <para><b>It does not subsume <see cref="PeriodParser"/>, and neither subsumes the other.</b>
    /// <b>THE AXES BELOW ARE KNOWN INSTANCES, NOT AN EXHAUSTIVE COUNT, and this docblock publishes
    /// no total on purpose.</b> The adjudicator is
    /// <c>DatePatternsDateOnlyLineTests</c> — read the `InlineData` there, not a number here. An
    /// earlier revision said "three axes", the next hardened it to "four", and both were wrong: the
    /// count is an emergent property of two independently written grammars, so any total is a claim
    /// that decays the moment either changes. Road 3 changes one of them.</para>
    ///
    /// <para><c>PeriodParser</c> is wider at least on: the word separators "till"/"to";
    /// single-digit months (<c>\d{1,2}</c> against this type's <c>\d{2}</c>); "." as a month
    /// separator where this type takes only "/"; "-" as a month separator <b>in the RIGHT point of
    /// a range</b> ("2020 – 03-2024") but NOT in a lone or left point ("03-2020"), because
    /// <c>SeparatorRegex</c> splits on the first separator IT ACCEPTS and <c>Split(trimmed, 2)</c>
    /// never splits the right part again — so a hyphen in a position that regex accepts as a split
    /// is consumed as one, and its
    /// "03" fails the point match, while a hyphen to the right of an accepted split reaches
    /// <c>PointRegex</c>'s <c>[/.\-]</c> branch intact; ISO <c>YYYY-MM</c> END points, because
    /// <see cref="DateRange"/>'s end-alternation is ordered so the bare <c>\d{4}</c> matches first
    /// and the word boundary after it holds against the following "-", leaving a non-empty tail;
    /// and a lone date POINT with no range separator ("03/2020"), which <see cref="DateRange"/>
    /// cannot match at all and <see cref="Year"/> reduces only to "03/", since "/" is not in the
    /// trailing-separator set.</para>
    ///
    /// <para>This predicate is wider at least on: a LEADING separator ("– 2020 – 2024"), which
    /// `PeriodParser`'s <c>^…$</c> anchoring refuses; and a range whose month is out of range
    /// ("13/2020 – 2024"), which <see cref="DateRange"/> matches structurally while `PeriodParser`
    /// validates the month and declines. So the callers take their UNION — substituting either for
    /// the other narrows suppression in one direction or the other.</para>
    ///
    /// <para><b>The ISO end-point axis is an alternation-ordering defect, and it is NOT
    /// review-only.</b>
    /// The same ordering inside <see cref="DateRange"/> reaches every surface that reads the match
    /// rather than the predicate: <c>HeadingDrivenResumeSegmenter.ExtractPeriod</c> returns the
    /// match VALUE, so "2020-06 – 2024-03" is stored as <c>Period = "2020-06 – 2024"</c> — the end
    /// month silently dropped from the value that rides into the promoted CV, on a path with no
    /// approve step. <see cref="StripDates"/> likewise leaves "-03" unmasked. One ordering, three
    /// surfaces. <b>The correction is the date-model widening's FIRST commit</b>
    /// (senior-cto-advisor re-bind 2026-08-02, bind 9): longest-alternative-first must land, pinned,
    /// BEFORE any alternation is added, or every new alternative reproduces the same defect —
    /// a trailing-qualifier alternative placed after the bare <c>\d{4}</c> would make
    /// "2020 – 2024 (heltid)" match "2024" first and leave the qualifier as a tail, exactly as this
    /// axis does now. Not corrected here: <see cref="DateRange"/> is deliberately unchanged by the
    /// promotion, whose segmenter half is a pure refactor.</para>
    /// </summary>
    public static bool IsDateOnlyLine(string line) => StripTrailingDate(line).Length == 0;

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(' ', '\t', ',', ';', '|', '-', '–', '—');
}
