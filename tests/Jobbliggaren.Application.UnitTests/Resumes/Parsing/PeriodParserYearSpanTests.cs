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
        string period, int expectedStart, int expectedEnd) =>
        AssertSpan(period, expectedStart, expectedEnd);

    [Theory]
    // #1060 road 3 — the month-NAME point, in both languages and all three lengths, plus the
    // YYYY/MM notation that widened with it. These are the rows the widening ADDED to this parser,
    // and they are here rather than merged into the theory above because they are the population
    // this file previously listed as free-text: "jan 2022 - juni 2024" was an InlineData in
    // TryParseYearSpan_NullEmptyOrFreeText_ReturnsFalse until the segmenter began producing it.
    //
    // The point of moving rather than deleting: the segmenter can EXTRACT these strings, so a
    // parser that refuses them drops a period the CV states — which costs A4/B6/B7 their verdicts
    // and costs OccupationExperienceDeriver the entry's years outright.
    [InlineData("jan 2022 - juni 2024", 2022, 2024)]
    [InlineData("januari 2022 – december 2024", 2022, 2024)]
    [InlineData("March 2019 – Sept 2021", 2019, 2021)]
    [InlineData("jan 2020 – nuvarande", 2020, CurrentYear)]
    // "2020/01 – 2024/12" passed through THIS theory for one commit (Klas-direktiv 2026-08-03: the
    // year-first SLASH notation was read as a month) and left it again in round 5 (decision D′):
    // PeriodParser's year-first branch is hyphen-only, so "2020/01" never matches PointRegex on
    // EITHER side of the range — this parser declines the whole string outright, independent of
    // whatever DatePatterns does with the LINE. It is free-text again from this type's point of
    // view, and the row is back in TryParseYearSpan_NullEmptyOrFreeText_ReturnsFalse below.
    [InlineData("maj 2020", 2020, 2020)]     // lone month point → zero-length span, parity with 03/2020
    public void TryParseYearSpan_MonthNamePoints_ResolveTheirSpan(
        string period, int expectedStart, int expectedEnd) =>
        AssertSpan(period, expectedStart, expectedEnd);

    private static void AssertSpan(string period, int expectedStart, int expectedEnd)
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
    // "jan 2022 - juni 2024" was a row here, on the grounds that month NAMES are not a recognised
    // point. #1060 road 3 made them one, so it is not free-text now — it is a period this parser
    // reads, and the segmenter can EXTRACT that string, so calling it free-text was dropping a
    // period the CV states. The row moved to TryParseYearSpan_MonthNamePoints_ResolveTheirSpan
    // below. (An earlier revision of this comment pointed at a test of that name before it existed:
    // the row had simply been deleted. Written down because a pointer to a test that is not there
    // reads exactly like coverage.)
    [InlineData("1899")]                     // below the 1900 lower year-guard → rejected
    // "2020/01 – 2024/12" — the reverse of the move above. It was briefly a row in the theory
    // above (Klas-direktiv 2026-08-03) and moved back HERE in round 5 (decision D′): the year-first
    // SLASH notation never matches PointRegex, on either endpoint, so this parser sees it as
    // unstructured text and refuses it — the same honest false as any other free-text period.
    [InlineData("2020/01 – 2024/12")]
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
