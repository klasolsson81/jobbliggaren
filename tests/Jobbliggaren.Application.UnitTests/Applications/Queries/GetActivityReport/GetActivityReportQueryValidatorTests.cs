using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Queries.GetActivityReport;

// #316 — defense-in-depth pre-handler-validering. Year/Month är både-eller-
// ingetdera (halv-specificerat par = klient-bugg, inte default). När båda finns
// är Month 1–12 och Year en sund gräns (2000–2100) så ett missformat
// ?year=0&month=99 returnerar en ren 400, inte en handler-tids-anomali.
public class GetActivityReportQueryValidatorTests
{
    private readonly GetActivityReportQueryValidator _validator = new();

    // ---------------------------------------------------------------
    // Både-eller-ingetdera
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithBothNull_IsValid()
    {
        // Default-fallet — handlern härleder innevarande svenska civilmånad.
        var result = _validator.Validate(new GetActivityReportQuery(null, null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithYearWithoutMonth_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, null));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMonthWithoutYear_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(null, 6));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithBothPresent_IsValid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, 6));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithHalfSpecifiedPair_MessageNamesTheCurrentMonthAsTheDefault()
    {
        // This is the 400's only sentence that states what the default IS, and it
        // shipped saying "föregående månad" while the handler resolved the current
        // Swedish civil month (Klas 2026-06-28). The string occurs exactly once in
        // the repo, and until this test nothing asserted on it, so a wrong sentence
        // sat in front of a correct implementation. That is the hole this pin closes.
        //
        // Who reads it: a direct API consumer, NOT a Jobbliggaren user. The web
        // client cannot reach this 400 at all — `page.tsx`'s `parseMonthParam`
        // rejects anything outside month 1-12 and year 2000-2100, which are the
        // validator's own bounds, before the single call site, and
        // `lib/api/applications.ts` sends year and month only together, so neither
        // failing rule is reachable from the browser. An earlier version of this
        // comment called the sentence "the only user-visible text", which is the
        // very overclaim this PR exists to remove.
        //
        // The two assertions have deliberately different strengths.
        // ShouldNotContain("föregående") pins the CLAIM: the wrong month may not
        // come back. ShouldContain("innevarande månad") pins the PHRASE, so a
        // rewording to "aktuell månad" fails here and goes through review rather
        // than landing silently.
        var result = _validator.Validate(new GetActivityReportQuery(2026, null));

        var message = result.Errors.ShouldHaveSingleItem().ErrorMessage;
        message.ShouldContain("innevarande månad");
        message.ShouldNotContain("föregående");
    }

    // ---------------------------------------------------------------
    // Month-gränser (1–12 inklusive)
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithMonthZero_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, 0));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMonthThirteen_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, 13));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMonthOne_IsValid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, 1));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithMonthTwelve_IsValid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2026, 12));

        result.IsValid.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Year-gränser (2000–2100 inklusive)
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithYearBelowLowerBound_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(1999, 6));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithYearAboveUpperBound_IsInvalid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2101, 6));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithYearAtLowerBound_IsValid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2000, 6));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithYearAtUpperBound_IsValid()
    {
        var result = _validator.Validate(new GetActivityReportQuery(2100, 6));

        result.IsValid.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Kombinerat missformat par (kärnan i defense-in-depth-spärren)
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithMalformedYearAndMonth_IsInvalid()
    {
        // ?year=0&month=99 — bägge utanför gräns → ren 400, inte handler-anomali.
        var result = _validator.Validate(new GetActivityReportQuery(0, 99));

        result.IsValid.ShouldBeFalse();
    }
}
