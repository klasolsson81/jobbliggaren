using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackPromptSuppressionTests
{
    [Fact]
    public void Record_KeepsTheOwnerThePageAndTheTime()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        var at = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        var suppression = FeedbackPromptSuppression.Record(owner, FeedbackPage.Matches, at);

        suppression.JobSeekerId.ShouldBe(owner);
        suppression.Page.ShouldBe(FeedbackPage.Matches);
        suppression.SuppressedAt.ShouldBe(at);
    }

    [Fact]
    public void Record_WithoutAnOwner_Throws()
        => Should.Throw<ArgumentException>(() =>
            FeedbackPromptSuppression.Record(default, FeedbackPage.Matches, DateTimeOffset.UnixEpoch));
}
