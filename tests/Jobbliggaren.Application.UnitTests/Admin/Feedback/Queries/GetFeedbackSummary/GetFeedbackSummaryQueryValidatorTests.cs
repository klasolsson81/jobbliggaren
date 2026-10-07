using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.GetFeedbackSummary;

/// <summary>#1979 — the summary's window is one of three fixed lengths, nothing in between.</summary>
public sealed class GetFeedbackSummaryQueryValidatorTests
{
    private readonly GetFeedbackSummaryQueryValidator _validator = new();

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public void Validate_AnAllowedWindow_Passes(int days) =>
        _validator.Validate(new GetFeedbackSummaryQuery(days)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-7)]
    [InlineData(14)]
    [InlineData(31)]
    [InlineData(365)]
    public void Validate_AnyOtherWindow_Fails(int days)
    {
        var result = _validator.Validate(new GetFeedbackSummaryQuery(days));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetFeedbackSummaryQuery.Days));
    }
}
