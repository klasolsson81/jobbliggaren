using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackCommentTests
{
    [Fact]
    public void Create_Text_IsTrimmed()
    {
        var result = FeedbackComment.Create("  Knappen hittas inte.  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Value.ShouldBe("Knappen hittas inte.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NoText_IsNoComment(string? text)
    {
        var result = FeedbackComment.Create(text);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void Create_AtTheLimit_Succeeds()
        => FeedbackComment.Create(new string('a', FeedbackComment.MaxLength)).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Create_OverTheLimitAfterTrimming_Fails()
    {
        var result = FeedbackComment.Create(" " + new string('a', FeedbackComment.MaxLength + 1) + " ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.CommentTooLong");
    }
}
