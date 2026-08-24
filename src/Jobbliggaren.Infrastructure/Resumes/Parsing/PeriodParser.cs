using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministically parses a CV experience period string (e.g. "01/2022 – 06/2024",
/// "2019–2021", "2020-06 – 2024-03" (ISO 8601), "jan 2020 – nuvarande") to a
/// start/end date + a format token (Fas 4 STEG 9, F4-9). Anchored to the full trimmed string so
/// free-text ("någon gång på 2020-talet",
/// "ett tag sen") does NOT parse — the conditional-Period criteria (A4/B6/B7) then report
/// NotAssessed rather than guess gaps/chronology from garbage (V-C, honest-data §5/OQ3).
/// <para>
/// <b>Its point grammar is one half of a pair and may not move alone (#1060 road 3).</b>
/// <see cref="DatePatterns.DateRange"/> is the other half: that regex's match VALUE is what
/// <c>HeadingDrivenResumeSegmenter.ExtractPeriod</c> STORES, and this type is what CONSUMES the
/// stored value. A form one side reaches and the other refuses is not an honest "not stated" — it
/// is a period the CV states and the product drops. The month-word list therefore has a single
/// home in <see cref="CvMonthNames"/>, read by both. The two are still allowed to disagree where
/// the disagreement is deliberate and documented: this type parses a LONE point ("2020",
/// "03/2020", "maj 2020") while <see cref="DatePatterns.IsDateOnlyLine"/> declines one, because
/// #428 settled that a lone date on a non-header line must not be read as a period.
/// </para>
/// <para>
/// Promoted to a neutral <c>Infrastructure/Resumes/Parsing</c> home (ADR 0079-amendment,
/// exp-per-occ PR-2): the F4-9 review engine, the F4-10 date-normalization transform AND
/// the import-time per-occupation experience attribution all parse a CV period, so the
/// single knowledge piece lives outside the review engine's namespace (DRY, CLAUDE.md §9.1).
/// </para>
/// </summary>
internal static partial class PeriodParser
{
    // A point is one of: MM<sep>YYYY (month-first, sep = / . or -), MONTHNAME YYYY ("jan 2020",
    // "december 2024"), YYYY-MM (year-first, ISO 8601 only — #420, the granularity the segmenter's
    // DateRangeRegex extracts), or a bare YYYY. Month and year land in the named groups regardless of
    // order; a month WORD lands in its own group and is resolved by CvMonthNames. The year-first
    // group is HYPHEN-ONLY — no slash alternative — and the paragraph below is why.
    //
    // YYYY/MM IS DELIBERATELY NOT A POINT FORM HERE, AND THAT IS A PRODUCT RULING (Klas-direktiv
    // 2026-08-03). The year-first SLASH notation is how Swedish writes a YEAR PAIR — a läsår or a
    // räkenskapsår, "2008/09", "2023/24" — not a year and a month. Nobody writes September 2008 as
    // "2008/09"; that is "2008-09", which this branch does read, because ISO 8601 says so.
    //
    // The widening briefly modelled the slash form here and read "2008/09 – 2011/12" as September
    // 2008 to December 2011, where the writer meant autumn 2008 to spring 2012. Measured before the
    // ruling. It also treated the notation INCONSISTENTLY: "2008/09" parsed (09 is a valid month
    // number) while "2019/20" did not (20 is not) — same notation, opposite outcomes, decided by an
    // accident of arithmetic rather than by what the form means.
    //
    // DatePatterns briefly kept RECOGNISING the form after this ruling landed — this type declined
    // to date it, DatePatterns.DateRange still matched it — and that split turned out not to be
    // free: DateRange's match VALUE is what ExtractPeriod stores, so a slash point beside an
    // UNRELATED, perfectly readable endpoint ("2020 – 2024/12") stored a value this type then
    // refused whole, where origin/main had stored a working bare-year degradation. Round 5 removed
    // the branch from DateRange too, on both endpoints — so the form is DATED by neither VALUE home,
    // origin/main's own answer, restored rather than repaired. See
    // DateRangeYearFirstCharacterisationTests for the measurement. ADR 0136 then gave the LINE
    // question its own grammar (DatePatterns.DateRowRange), which recognises the form as a date ROW
    // and produces no stored value — so this type's reading is unchanged and must stay so: it is
    // what keeps round 5's Blocker closed.
    //
    // THE MONTH-NAME FORM IS HERE BECAUSE DatePatterns.DateRange MATCHES IT, and
    // the two must widen together (senior-cto-advisor re-bind 2026-08-03, Approach A). DateRange's
    // match VALUE is what HeadingDrivenResumeSegmenter.ExtractPeriod stores as
    // ParsedExperience.Period, and this type is what consumes that value — so a form the segmenter
    // can extract but this parser refuses is not an honest "not stated", it is a period the CV
    // states and the product then drops. Measured when the two were briefly out of step:
    // "jan 2020 – nuvarande" stored a parseable "2020 – nuvarande" before the widening and an
    // unparseable "jan 2020 – nuvarande" after, costing A4/B6/B7 their verdicts and costing
    // OccupationExperienceDeriver the entry's years outright. The month list has ONE home,
    // CvMonthNames, shared with DateRange; a copy here would recreate that defect.
    //
    // IgnoreCase is required now that a branch matches letters ("Jan 2020", "DEC 2024"); it was
    // absent while every branch was digits-only. CultureInvariant stays: the token set is a lexical
    // index, never culture-sensitive casing.
    [GeneratedRegex(
        @"^(?:(?<month>\d{1,2})[/.\-](?<year>\d{4})|(?<monthName>" + CvMonthNames.Pattern + ")"
        + CvMonthNames.AfterName + @"(?<year>\d{4})|(?<year>\d{4})(?:-(?<month>\d{2}))?)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PointRegex();

    // Range separators: en/em dash, an ASCII hyphen, or the words "till"/"to" (spaces optional).
    // The ASCII hyphen is ambiguous — it is the range split in "2019-2021" (a \d{4}-\d{4} year
    // range) but the MONTH separator inside a "2020-06" ISO point (\d{4}-\d{2}), #420. So a hyphen
    // is NOT a range split when it sits between exactly four digits and exactly two (a point-
    // internal month hyphen); "\d{4}-\d{4}" still splits (its right side has four digits, not two).
    [GeneratedRegex(
        @"\s*(?:[–—]|(?<!\d{4})-|-(?!\d{2}(?!\d))|\btill\b|\bto\b)\s*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SeparatorRegex();

    // Derived from the SAME const DateRange's end-alternation is built from, so the two cannot
    // drift (architect R3). DateRange matching a keyword this parser does not know would store a
    // period the product then drops — the K1 failure mode, one lexicon over.
    private static readonly string[] PresentKeywords = CvMonthNames.PresentKeywords.Split('|');

    /// <summary>
    /// Attempts to parse <paramref name="period"/>. Returns true with start/end dates and a
    /// format token (e.g. "MM/YYYY", "YYYY") when the whole trimmed string is a recognised
    /// date point or range; false for null/empty/free-text.
    /// </summary>
    public static bool TryParse(
        string? period, out DateOnly start, out DateOnly end, out string? formatToken)
    {
        start = default;
        end = default;
        formatToken = null;

        if (string.IsNullOrWhiteSpace(period))
        {
            return false;
        }

        var trimmed = period.Trim();

        // Split into at most two points on the first separator occurrence.
        var parts = SeparatorRegex().Split(trimmed, 2);

        if (parts.Length == 1)
        {
            // Single point — start == end.
            if (!TryParsePoint(parts[0], isEnd: false, out start, out var fmt))
            {
                return false;
            }

            end = start;
            formatToken = fmt;
            return true;
        }

        var left = parts[0].Trim();
        var right = parts[1].Trim();

        if (!TryParsePoint(left, isEnd: false, out start, out var startFmt))
        {
            return false;
        }

        if (IsPresent(right))
        {
            // Ongoing role — sentinel future end so gap/chronology maths still work without a clock.
            end = DateOnly.MaxValue;
            formatToken = startFmt;
            return true;
        }

        if (!TryParsePoint(right, isEnd: true, out end, out var endFmt))
        {
            return false;
        }

        // The format token reflects the granularity (MM/YYYY vs YYYY); for a mixed-granularity
        // range, the coarser token wins so B6 flags the inconsistency at the entry level.
        formatToken = startFmt == endFmt ? startFmt : "YYYY";
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="period"/> to a calendar-year span (ADR 0079-amendment,
    /// exp-per-occ PR-2). Builds on <see cref="TryParse"/> and adds the clock-aware
    /// "present" resolution that a year-COUNT needs but gap-math does not: an ongoing role
    /// (<c>nuvarande/idag/nu/…</c>) resolves its end to <paramref name="currentYear"/> (the
    /// caller passes <c>IDateTimeProvider.UtcNow.Year</c> — never <c>DateTime.Now</c>,
    /// CLAUDE.md §5). Year granularity is deliberate: month precision is noise for a "~N år"
    /// estimate and invites false precision. The span is <c>endYear - startYear</c>, so
    /// "2019–2021" yields 2 (not 3 calendar years) and a bare year-only point ("2020") yields
    /// start==end → a zero-length span (the caller attributes 0 years, distinct from "not
    /// stated"). Returns false for null/empty/free-text (honest "not stated") AND for a
    /// malformed reverse range whose end precedes its start (so the caller never attributes a
    /// negative span).
    /// </summary>
    public static bool TryParseYearSpan(
        string? period, int currentYear, out int startYear, out int endYear)
    {
        startYear = 0;
        endYear = 0;

        if (!TryParse(period, out var start, out var end, out _))
        {
            return false;
        }

        startYear = start.Year;
        endYear = end == DateOnly.MaxValue ? currentYear : end.Year;

        // A reverse range ("2024 – 2019") or a future-dated ongoing role ("2030 – nu" before
        // 2030) is malformed for a year count — reject rather than count a negative span.
        return endYear >= startYear;
    }

    private static bool IsPresent(string token) =>
        PresentKeywords.Contains(token.ToLowerInvariant());

    private static bool TryParsePoint(string token, bool isEnd, out DateOnly date, out string formatToken)
    {
        date = default;
        formatToken = string.Empty;

        var match = PointRegex().Match(token.Trim());
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        if (year is < 1900 or > 2100)
        {
            return false;
        }

        // A month WORD resolves through CvMonthNames and then joins the numeric path below, so it
        // gets the same range validation and the same "MM/YYYY" token — one month-granularity
        // concept, not a second one. A word the pattern matched but the map does not know is a
        // contradiction between two halves of one home; refuse rather than guess a month, and
        // CvMonthNamesTests pins the correspondence in both directions so it cannot happen quietly.
        if (match.Groups["monthName"].Success)
        {
            if (!CvMonthNames.TryGetOrdinal(match.Groups["monthName"].Value, out var named))
            {
                return false;
            }

            date = new DateOnly(year, named, 1);
            formatToken = "MM/YYYY";
            return true;
        }

        if (match.Groups["month"].Success)
        {
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12)
            {
                return false;
            }

            date = new DateOnly(year, month, 1);
            // Month granularity → the "MM/YYYY" token regardless of the source notation (MM/YYYY,
            // MM-YYYY or ISO YYYY-MM). B6 verdicts on the DISTINCT token set (StructureRules B6),
            // so an ISO point and a slash point read as ONE consistent format, not "blandade" (#420).
            formatToken = "MM/YYYY";
            return true;
        }

        // Year-only: a start anchors to January, an end to December, so a "2019–2021"
        // role spans the whole interval for gap maths.
        date = new DateOnly(year, isEnd ? 12 : 1, 1);
        formatToken = "YYYY";
        return true;
    }
}
