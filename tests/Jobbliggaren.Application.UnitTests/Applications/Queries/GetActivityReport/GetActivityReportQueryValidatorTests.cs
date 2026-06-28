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
        // Default-fallet — handlern härleder föregående månad.
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
