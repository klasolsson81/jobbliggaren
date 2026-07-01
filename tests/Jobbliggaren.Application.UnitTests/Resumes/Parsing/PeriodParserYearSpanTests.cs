using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// ADR 0079-amendment (exp-per-occ PR-2) — the clock-aware year-span helper on the promoted
/// <see cref="PeriodParser"/>. Builds on the F4-9 <c>TryParse</c> (covered by the review-engine
/// suite) and adds: present → injected current year (never DateTime.Now), year granularity,
/// honest false for free-text AND for a malformed reverse/future span (no negative attribution).
/// </summary>
public class PeriodParserYearSpanTests
{
    private const int CurrentYear = 2026;

    [Theory]
    [InlineData("2019–2021", 2019, 2021)]   // en-dash year range
    [InlineData("2019—2021", 2019, 2021)]   // em-dash year range (CV input, not UI copy)
    [InlineData("2019-2021", 2019, 2021)]   // hyphen range
    [InlineData("2019 till 2021", 2019, 2021)]
    [InlineData("2019 to 2021", 2019, 2021)] // English "to" separator
    [InlineData("01/2020 – 06/2024", 2020, 2024)]
    [InlineData("2019 - 06/2024", 2019, 2024)] // mixed granularity (year start, MM/YYYY end)
    [InlineData("2020-06 – 2024-03", 2020, 2024)] // #420: ISO 8601 YYYY-MM range the segmenter extracts — the hyphen INSIDE a point is the month separator, not the range split
    [InlineData("2020-06-2024-03", 2020, 2024)]   // #420: spaceless ISO range — a hyphen with a 4-digit year on its right still splits; a 2-digit month on its right does not
    [InlineData("2019", 2019, 2019)]        // single year-only point → zero-length span (→ 0, NOT null; #191/ADR 0079 Variant A — a bare year IS parseable)
    [InlineData("03/2020", 2020, 2020)]     // single MM/YYYY point → zero-length span (same #191 rule)
    [InlineData("2020-06", 2020, 2020)]     // #420: single ISO YYYY-MM point → zero-length span (month granularity, parity with 03/2020)
    public void TryParseYearSpan_RecognisedRangeOrPoint_ReturnsYearBounds(
        string period, int expectedStart, int expectedEnd)
    {
        var ok = PeriodParser.TryParseYearSpan(period, CurrentYear, out var start, out var end);

        ok.ShouldBeTrue();
        start.ShouldBe(expectedStart);
        end.ShouldBe(expectedEnd);
    }

    [Theory]
    [InlineData("2005 – nuvarande")]
    [InlineData("2005 – nu")]
    [InlineData("03/2005 – pågående")]
    [InlineData("2005 – present")]
    public void TryParseYearSpan_OngoingRole_ResolvesEndToCurrentYear(string period)
    {
        var ok = PeriodParser.TryParseYearSpan(period, CurrentYear, out var start, out var end);

        ok.ShouldBeTrue();
        start.ShouldBe(2005);
        end.ShouldBe(CurrentYear); // the injected clock year, never DateTime.Now
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ett tag sedan")]            // free-text → honest false (never guessed)
    [InlineData("någon gång på 2020-talet")]
    [InlineData("jan 2022 - juni 2024")]     // month NAMES are not a recognised point
    [InlineData("1899")]                     // below the 1900 lower year-guard → rejected
    public void TryParseYearSpan_NullEmptyOrFreeText_ReturnsFalse(string? period)
    {
        var ok = PeriodParser.TryParseYearSpan(period, CurrentYear, out var start, out var end);

        ok.ShouldBeFalse();
        start.ShouldBe(0);
        end.ShouldBe(0);
    }

    [Fact]
    public void TryParseYearSpan_ReverseRange_ReturnsFalse()
    {
        // A backwards range cannot yield a non-negative count — reject rather than attribute a
        // negative span.
        var ok = PeriodParser.TryParseYearSpan("2024 – 2019", CurrentYear, out _, out _);

        ok.ShouldBeFalse();
    }

    [Fact]
    public void TryParseYearSpan_FutureDatedOngoingRole_ReturnsFalse()
    {
        // "2030 – nu" evaluated before 2030: present resolves to the current year (2026), which
        // precedes the start — malformed for a year count, so honest false.
        var ok = PeriodParser.TryParseYearSpan("2030 – nu", CurrentYear, out _, out _);

        ok.ShouldBeFalse();
    }
}
