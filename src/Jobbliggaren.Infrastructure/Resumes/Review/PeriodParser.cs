using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Review;

/// <summary>
/// Deterministically parses a CV experience period string (e.g. "01/2022 – 06/2024",
/// "2019–2021", "03/2020 – nuvarande") to a start/end date + a format token (Fas 4 STEG 9,
/// F4-9). Anchored to the full trimmed string so free-text ("någon gång på 2020-talet",
/// "ett tag sen") does NOT parse — the conditional-Period criteria (A4/B6/B7) then report
/// NotAssessed rather than guess gaps/chronology from garbage (V-C, honest-data §5/OQ3).
/// </summary>
internal static partial class PeriodParser
{
    // A point is an optional month (MM with / or . or - separator) + a 4-digit year.
    [GeneratedRegex(@"^(?:(\d{1,2})[/.\-])?(\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PointRegex();

    // Range separators: en/em dash, hyphen, or the words "till"/"to" (spaces optional).
    [GeneratedRegex(@"\s*(?:[–—-]|\btill\b|\bto\b)\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
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

        var year = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (year is < 1900 or > 2100)
        {
            return false;
        }

        if (match.Groups[1].Success)
        {
            var month = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12)
            {
                return false;
            }

            date = new DateOnly(year, month, 1);
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
