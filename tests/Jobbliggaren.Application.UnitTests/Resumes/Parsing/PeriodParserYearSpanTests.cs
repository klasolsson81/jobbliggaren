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
    [InlineData("2019-2021", 2019, 2021)]   // hyphen range
    [InlineData("2019 till 2021", 2019, 2021)]
    [InlineData("01/2020 – 06/2024", 2020, 2024)]
    [InlineData("2019", 2019, 2019)]        // single year-only point → zero-length span
    [InlineData("03/2020", 2020, 2020)]     // single MM/YYYY point
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
