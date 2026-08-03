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
    // present-keyword.
    //
    // BOTH POINT ALTERNATIONS ARE ORDERED LONGEST-ALTERNATIVE-FIRST, AND THAT ORDER IS THE
    // CONTRACT — not a formatting preference. .NET's alternation is ordered and non-greedy across
    // branches: the first branch that lets the OVERALL match succeed wins, and nothing forces
    // backtracking to a longer one afterwards. With the bare `\d{4}` written first, "2020-06 –
    // 2024-03" matched only as far as "2024" — the `\b` after it holds against the following "-",
    // the overall match succeeds, and the end month is dropped. That reached three surfaces at
    // once (IsDateOnlyLine, ExtractPeriod's stored VALUE, StripDates' masking), so it was one
    // ordering defect and not three bugs.
    //
    // The START alternation never showed the defect, because there a too-short branch makes the
    // overall match FAIL and the engine backtracks into the longer one. It is ordered anyway: the
    // rule has to hold by construction rather than by which side happens to be rescued by
    // backtracking, or the next alternative added to the wrong list reintroduces it silently.
    //
    // WHY THIS LANDED BEFORE ANY ALTERNATIVE WAS ADDED (senior-cto-advisor re-bind 2026-08-02,
    // bind 9): every new alternative is a chance to reproduce it. A trailing-qualifier branch
    // placed after `\d{4}` would make "2020 – 2024 (heltid)" match "2024" and leave the qualifier
    // as a tail; a "jan" branch placed before "januari" would match "januari 2020" as far as
    // "jan". Adding alternatives to an unordered list compounds the defect; adding them to an
    // ordered one does not.
    // THE POINT FORMS, and each is a modelling decision rather than a pattern that grew:
    //   MONTHNAME YYYY   "jan 2020", "januari 2020", "Dec. 2024"  (Swedish and English, IgnoreCase)
    //   YYYY-MM          ISO 8601
    //   YYYY/MM          the slash-written year-first form
    //   MM/YYYY          the slash-written month-first form
    //   YYYY             a bare year
    // A month NAME point is deliberately only recognised INSIDE a range. A lone "maj 2020" is not
    // reduced (Year() takes the "2020" and leaves "maj"), exactly as a lone "03/2020" is not — the
    // range separator is what disambiguates a period from a date mentioned in prose, and #428
    // already settled that a lone bare year on a non-header line must NOT be read as a period.
    // Widening the LONE-point case is a further change with a different blast radius, and it is
    // not this one; the residual is named in IsDateOnlyLine's docblock rather than left implicit.
    [GeneratedRegex(
        @"\b((?:februari|september|february|november|december|januari|january|augusti|oktober|october|august|march|april|mars|juni|june|juli|july|sept|maj|may|jan|feb|mar|apr|jun|jul|aug|sep|okt|oct|nov|dec)\.?\s+\d{4}|\d{4}-\d{2}|\d{4}/\d{2}|\d{2}/\d{4}|\d{4})\s*[-–—]\s*((?:februari|september|february|november|december|januari|january|augusti|oktober|october|august|march|april|mars|juni|june|juli|july|sept|maj|may|jan|feb|mar|apr|jun|jul|aug|sep|okt|oct|nov|dec)\.?\s+\d{4}|\d{4}-\d{2}|\d{4}/\d{2}|\d{2}/\d{4}|\d{4}|nuvarande|pågående|pagaende|present|current|now|idag|nu)\b",
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
        if (range.Success && IsIgnorableTail(line[(range.Index + range.Length)..]))
            return TrimTrailingSeparators(line[..range.Index]);

        var year = Year().Match(line);
        if (year.Success && IsIgnorableTail(line[(year.Index + year.Length)..]))
            return TrimTrailingSeparators(line[..year.Index]);

        return line;
    }

    // What may follow the match and still leave the line "nothing but a date".
    //
    // TWO OF THE FOUR WIDENED FORMS LIVE HERE AND NOT IN DateRange, AND THE SPLIT IS THE DESIGN.
    // DateRange's match VALUE is what ExtractPeriod stores as ParsedExperience.Period, so anything
    // added there rides into the promoted CV and must survive PeriodParser. A trailing qualifier
    // does not: modelling "2020 – 2024 (heltid)" inside DateRange would store
    // Period = "2020 – 2024 (heltid)", which PeriodParser refuses — turning a period that parses
    // today into one that does not, and losing A4/B6/B7 on an entry that currently has them. That
    // is a REGRESSION dressed as a widening. Asking instead "does anything but a date follow?" adds
    // the suppression without touching the stored value: Period stays "2020 – 2024".
    //
    // The keyword-less open end ("2020 –") is here for the second half of the same reason. An
    // empty-end alternative in DateRange would match mid-prose ("Acme AB 2020 - Systemutvecklare"),
    // widening the MASK far beyond the line-level question actually being asked.
    //
    // THE ACCEPTED SET IS DELIBERATELY NARROWER THAN TrimTrailingSeparators'. That method trims
    // "," ";" "|" from the text to the LEFT of the match; this one does not accept them to the
    // RIGHT, so "2005 - 2010," stays a non-date-only line and its pin stays green. The asymmetry is
    // real and intended: a separator BEFORE the date sits between fields that were there, while a
    // glyph AFTER it is the start of something the line still carries. A parenthesised trailer is
    // the exception because a bracketed group is self-delimiting — "2020 – 2024, Acme AB" must stay
    // a line with an employer on it, and only the brackets tell the two apart.
    private static bool IsIgnorableTail(string tail)
    {
        var rest = tail.Trim();
        if (rest.Length == 0)
            return true;

        // A dangling range separator, with or without a bracketed qualifier behind it.
        rest = rest.TrimStart(' ', '\t', '-', '–', '—').TrimStart();
        if (rest.Length == 0)
            return true;

        return rest.Length > 1
            && rest[0] == '('
            && rest[^1] == ')'
            && !rest[1..^1].Contains('(')
            && !rest[1..^1].Contains(')');
    }

    /// <summary>
    /// True when a date match runs to the END of <paramref name="line"/> and nothing but separators
    /// precedes it — "the line carries a date and nothing else". <b>What may FOLLOW the match is
    /// whitespace, a dangling range separator, or a single bracketed qualifier — and nothing
    /// else</b>, so <c>"2005 - 2010,"</c> is still false while <c>"2020 –"</c> and
    /// <c>"2020 – 2024 (heltid)"</c> are true; <see cref="IsIgnorableTail"/> owns that set and the
    /// reason it is narrower than the trim applied to the LEFT of the match. Also true VACUOUSLY for
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
    /// that decays the moment either changes. Road 3 changed one of them, and the lists below moved
    /// with it — in BOTH directions, which is why no total survives the move.</para>
    ///
    /// <para><b>What road 3 added, and what it deliberately did not.</b> The date model now reaches
    /// month names ("jan 2020 – dec 2024"), <c>YYYY/MM</c> ("2020/01 – 2024/12"), a trailing
    /// bracketed qualifier ("2020 – 2024 (heltid)") and a keyword-less open end ("2020 –"). The
    /// first two are POINT forms and live in <see cref="DateRange"/>; the last two are properties of
    /// the LINE and live in <see cref="IsIgnorableTail"/>, because <see cref="DateRange"/>'s match
    /// value is what <c>ExtractPeriod</c> stores and a qualifier inside it would break the stored
    /// period rather than widen it. <b>A LONE month point ("maj 2020") is still not reduced</b> —
    /// only ranges are — exactly as a lone "03/2020" is not. That residual is named rather than
    /// left implicit: it is a further widening with a different blast radius, and it is not this
    /// one. Nor did <see cref="PeriodParser"/> move: it still refuses month names and
    /// <c>YYYY/MM</c>, so those forms now yield a Period that is EXTRACTED but not PARSEABLE. That
    /// is benign and was verified rather than assumed — <c>CvReviewEngine</c> falls to
    /// <c>DatedExperience(experience, null, null, null)</c>, identical to an absent period, so
    /// A4/B6/B7 report the same NotAssessed they reported when the value was null. Teaching
    /// <c>PeriodParser</c> these forms would ASSESS them instead, which is a different
    /// change-reason and a follow-up.</para>
    ///
    /// <para><c>PeriodParser</c> is wider at least on: the word separators "till"/"to";
    /// single-digit months (<c>\d{1,2}</c> against this type's <c>\d{2}</c>); "." as a month
    /// separator where this type takes only "/"; "-" as a month separator <b>in the RIGHT point of
    /// a range</b> ("2020 – 03-2024") but NOT in a lone or left point ("03-2020"), because
    /// <c>SeparatorRegex</c> splits on the first separator IT ACCEPTS and <c>Split(trimmed, 2)</c>
    /// never splits the right part again — so a hyphen in a position that regex accepts as a split
    /// is consumed as one, and its
    /// "03" fails the point match, while a hyphen to the right of an accepted split reaches
    /// <c>PointRegex</c>'s <c>[/.\-]</c> branch intact;
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
    /// <para><b>The ISO end-point axis WAS an alternation-ordering defect, and it is CORRECTED
    /// (#1060 road 3, commit 1).</b> It is recorded here rather than deleted because the shape
    /// recurs: the ordering inside <see cref="DateRange"/> reached every surface that reads the
    /// match rather than the predicate, so one token of order was three defects.
    /// <c>HeadingDrivenResumeSegmenter.ExtractPeriod</c> returns the match VALUE, so
    /// "2020-06 – 2024-03" was stored as <c>Period = "2020-06 – 2024"</c> — the end month dropped
    /// from the value that rides into the promoted CV, on a path with no approve step — and
    /// <see cref="StripDates"/> left "-03" unmasked, so a prose bullet carrying an inline ISO range
    /// read as carrying a measurable digit. Downstream, the truncated value degraded
    /// <c>PeriodParser</c>'s format token from <c>MM/YYYY</c> to <c>YYYY</c>, which beside a
    /// slash-formatted entry produced a false "Blandade datumformat" Warn from B6 on a CV that is
    /// consistent at month granularity — the defect #420 exists to prevent. All four were measured
    /// before the correction and re-measured after; the ordering rationale lives on
    /// <see cref="DateRange"/> itself, and <c>DatePatternsAlternationOrderingTests</c> is the
    /// adjudicator.</para>
    /// </summary>
    public static bool IsDateOnlyLine(string line) => StripTrailingDate(line).Length == 0;

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(' ', '\t', ',', ';', '|', '-', '–', '—');
}
