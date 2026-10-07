using Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;
using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.ListFeedback;

/// <summary>#1979 — the list's bounds: a page key from the fixed set, a defined status, and a bounded page.</summary>
public sealed class ListFeedbackQueryValidatorTests
{
    private readonly ListFeedbackQueryValidator _validator = new();

    [Fact]
    public void Validate_TheDefaults_Pass() => _validator.Validate(new ListFeedbackQuery()).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_AKnownPageAndStatusAtTheLargestPage_Pass() =>
        _validator.Validate(new ListFeedbackQuery(FeedbackStatus.Declined, "cv-review", 7, ListFeedbackQuery.MaxPageSize))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("nope")]
    [InlineData("Jobs")]
    [InlineData("")]
    public void Validate_APageKeyOutsideTheFixedSet_Fails(string pageKey)
    {
        var result = _validator.Validate(new ListFeedbackQuery(PageKey: pageKey));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ListFeedbackQuery.PageKey));
    }

    [Fact]
    public void Validate_AnUndefinedStatus_Fails() =>
        _validator.Validate(new ListFeedbackQuery(Status: (FeedbackStatus)42)).Errors
            .ShouldContain(e => e.PropertyName == nameof(ListFeedbackQuery.Status));

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, ListFeedbackQuery.MaxPageSize + 1)]
    public void Validate_APageOutsideTheBounds_Fails(int pageNumber, int pageSize) =>
        _validator.Validate(new ListFeedbackQuery(PageNumber: pageNumber, PageSize: pageSize)).IsValid.ShouldBeFalse();
}
