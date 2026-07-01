using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministically parses a CV experience period string (e.g. "01/2022 – 06/2024",
/// "2019–2021", "2020-06 – 2024-03" (ISO 8601), "03/2020 – nuvarande") to a start/end date + a
/// format token (Fas 4 STEG 9, F4-9). Anchored to the full trimmed string so free-text
/// ("någon gång på 2020-talet",
/// "ett tag sen") does NOT parse — the conditional-Period criteria (A4/B6/B7) then report
/// NotAssessed rather than guess gaps/chronology from garbage (V-C, honest-data §5/OQ3).
/// <para>
/// Promoted to a neutral <c>Infrastructure/Resumes/Parsing</c> home (ADR 0079-amendment,
/// exp-per-occ PR-2): the F4-9 review engine, the F4-10 date-normalization transform AND
/// the import-time per-occupation experience attribution all parse a CV period, so the
/// single knowledge piece lives outside the review engine's namespace (DRY, CLAUDE.md §9.1).
/// </para>
/// </summary>
internal static partial class PeriodParser
{
    // A point is one of: MM<sep>YYYY (month-first, sep = / . or -), YYYY-MM (ISO 8601 year-first,
    // #420 — the granularity the segmenter's DateRangeRegex extracts), or a bare YYYY. Month and
    // year land in the named groups regardless of order; the ISO month is exactly two digits.
    [GeneratedRegex(
        @"^(?:(?<month>\d{1,2})[/.\-](?<year>\d{4})|(?<year>\d{4})(?:-(?<month>\d{2}))?)$",
        RegexOptions.CultureInvariant)]
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

    private static readonly string[] PresentKeywords =
        ["nuvarande", "idag", "pågående", "pagaende", "present", "current", "now", "nu"];

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
