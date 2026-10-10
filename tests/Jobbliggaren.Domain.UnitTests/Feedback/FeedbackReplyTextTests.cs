using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackReplyTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void Create_NothingToSend_IsRefused(string? text)
    {
        var result = FeedbackReplyText.Create(text);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.ReplyEmpty");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Create_TrimsAndKeepsTheLinesAsLineFeeds()
    {
        var text = FeedbackReplyText.Create("  Hej,\r\n\r\ntack för din feedback.\rVi tittar på det.  ").Value;

        text.Value.ShouldBe("Hej,\n\ntack för din feedback.\nVi tittar på det.");
    }

    [Fact]
    public void Create_AtTheLimit_IsAccepted()
    {
        var text = FeedbackReplyText.Create(new string('a', FeedbackReplyText.MaxLength));

        text.IsSuccess.ShouldBeTrue();
        text.Value.Value.Length.ShouldBe(FeedbackReplyText.MaxLength);
    }

    [Fact]
    public void Create_OverTheLimit_IsRefused()
    {
        var result = FeedbackReplyText.Create(new string('a', FeedbackReplyText.MaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.ReplyTooLong");
    }

    // A pair of CRLF counts as one character once normalised, so the limit is measured on what is stored.
    [Fact]
    public void Create_TheLimitIsMeasuredAfterTheLineEndingsAreNormalised()
    {
        var raw = new string('a', FeedbackReplyText.MaxLength - 1) + "\r\nb";

        FeedbackReplyText.Create(raw).IsFailure.ShouldBeTrue();
        FeedbackReplyText.Create(new string('a', FeedbackReplyText.MaxLength - 2) + "\r\nb").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ToString_NeverCarriesTheText()
    {
        var text = FeedbackReplyText.Create("Hemligt svar").Value;

        text.ToString().ShouldNotContain("Hemligt");
    }
}
