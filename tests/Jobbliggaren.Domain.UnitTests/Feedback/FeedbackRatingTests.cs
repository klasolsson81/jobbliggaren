using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_OneToFive_Succeeds(int value)
    {
        var result = FeedbackRating.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Create_OutsideOneToFive_FailsWithItsCode(int value)
    {
        var result = FeedbackRating.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.RatingOutOfRange");
    }
}
