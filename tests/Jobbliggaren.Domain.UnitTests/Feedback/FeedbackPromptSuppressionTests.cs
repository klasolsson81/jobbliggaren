using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackPromptSuppressionTests
{
    [Fact]
    public void Record_KeepsTheOwnerAndThePage()
    {
        var owner = new JobSeekerId(Guid.NewGuid());

        var suppression = FeedbackPromptSuppression.Record(owner, FeedbackPage.Matches);

        suppression.JobSeekerId.ShouldBe(owner);
        suppression.Page.ShouldBe(FeedbackPage.Matches);
    }

    [Fact]
    public void Record_WithoutAnOwner_Throws()
        => Should.Throw<ArgumentException>(() =>
            FeedbackPromptSuppression.Record(default, FeedbackPage.Matches));
}
