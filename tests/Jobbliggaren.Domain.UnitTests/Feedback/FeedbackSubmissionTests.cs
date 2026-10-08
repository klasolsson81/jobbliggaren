using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.Feedback.Events;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

public class FeedbackSubmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobSeekerId Owner = new(Guid.NewGuid());

    private static FeedbackRating Stars(int value) => FeedbackRating.Create(value).Value;

    private static FeedbackComment Comment(string text) => FeedbackComment.Create(text).Value!;

    private static FeedbackSubmission Submit(FeedbackRating? rating, FeedbackComment? comment) =>
        FeedbackSubmission.Submit(Owner, Guid.NewGuid(), FeedbackPage.Jobs, rating, comment,
            ReportedClientContext.Empty, appVersion: null, Now).Value;

    [Fact]
    public void Submit_RatingOnly_IsANewSubmission()
    {
        var key = Guid.NewGuid();
        var result = FeedbackSubmission.Submit(Owner, key, FeedbackPage.JobAd, Stars(4), null,
            ReportedClientContext.Empty, "9e5c37129", Now);

        result.IsSuccess.ShouldBeTrue();
        var submission = result.Value;
        submission.JobSeekerId.ShouldBe(Owner);
        submission.SubmissionKey.ShouldBe(key);
        submission.Page.ShouldBe(FeedbackPage.JobAd);
        submission.Rating.ShouldBe(Stars(4));
        submission.Comment.ShouldBeNull();
        submission.AppVersion.ShouldBe("9e5c37129");
        submission.Status.ShouldBe(FeedbackStatus.New);
        submission.SubmittedAt.ShouldBe(Now);
        submission.StatusChangedAt.ShouldBeNull();
    }

    [Fact]
    public void Submit_TextOnly_IsANewSubmission()
    {
        var submission = Submit(rating: null, Comment("Filtret glömmer min ort."));

        submission.Rating.ShouldBeNull();
        submission.Comment!.Value.ShouldBe("Filtret glömmer min ort.");
    }

    [Fact]
    public void Submit_NeitherRatingNorText_IsRefused()
    {
        var result = FeedbackSubmission.Submit(Owner, Guid.NewGuid(), FeedbackPage.Jobs, null, null,
            ReportedClientContext.Empty, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.Empty");
    }

    [Fact]
    public void Submit_WithoutAnOwner_IsRefused()
        => FeedbackSubmission.Submit(default, Guid.NewGuid(), FeedbackPage.Jobs, Stars(3), null,
                ReportedClientContext.Empty, null, Now)
            .Error.Code.ShouldBe("Feedback.OwnerRequired");

    [Fact]
    public void Submit_WithoutASubmissionKey_IsRefused()
        => FeedbackSubmission.Submit(Owner, Guid.Empty, FeedbackPage.Jobs, Stars(3), null,
                ReportedClientContext.Empty, null, Now)
            .Error.Code.ShouldBe("Feedback.SubmissionKeyRequired");

    [Theory]
    [InlineData("dev")]
    [InlineData("9E5C37129")]
    [InlineData("anna-svensson")]
    [InlineData("9e5c37")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("abc1234\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567\n")]
    public void Submit_AnAppVersionThatIsNotACommitHash_IsRefused(string appVersion)
        => FeedbackSubmission.Submit(Owner, Guid.NewGuid(), FeedbackPage.Jobs, Stars(3), null,
                ReportedClientContext.Empty, appVersion, Now)
            .Error.Code.ShouldBe("Feedback.AppVersionInvalid");

    [Fact]
    public void Submit_RaisesFeedbackSubmitted()
    {
        var submission = Submit(Stars(5), null);

        submission.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<FeedbackSubmittedDomainEvent>()
            .SubmissionId.ShouldBe(submission.Id);
    }

    [Theory]
    [InlineData(FeedbackStatus.InProgress)]
    [InlineData(FeedbackStatus.Resolved)]
    [InlineData(FeedbackStatus.Declined)]
    public void ChangeStatus_ToAnotherStatus_StampsTheTime(FeedbackStatus to)
    {
        var submission = Submit(Stars(2), null);
        var later = Now.AddHours(3);

        submission.ChangeStatus(to, later).IsSuccess.ShouldBeTrue();

        submission.Status.ShouldBe(to);
        submission.StatusChangedAt.ShouldBe(later);
    }

    [Fact]
    public void ChangeStatus_RaisesFeedbackStatusChanged()
    {
        var submission = Submit(Stars(2), null);
        var later = Now.AddHours(3);

        submission.ChangeStatus(FeedbackStatus.Resolved, later).IsSuccess.ShouldBeTrue();

        var changed = submission.DomainEvents.OfType<FeedbackStatusChangedDomainEvent>().ShouldHaveSingleItem();
        changed.SubmissionId.ShouldBe(submission.Id);
        changed.From.ShouldBe(FeedbackStatus.New);
        changed.To.ShouldBe(FeedbackStatus.Resolved);
        changed.OccurredAt.ShouldBe(later);
    }

    [Fact]
    public void ChangeStatus_ReopeningAClosedItem_IsAllowed()
    {
        var submission = Submit(Stars(2), null);
        submission.ChangeStatus(FeedbackStatus.Resolved, Now).IsSuccess.ShouldBeTrue();

        submission.ChangeStatus(FeedbackStatus.InProgress, Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        submission.Status.ShouldBe(FeedbackStatus.InProgress);
    }

    [Fact]
    public void ChangeStatus_ToTheSameStatus_IsRefusedAndChangesNothing()
    {
        var submission = Submit(Stars(2), null);

        var result = submission.ChangeStatus(FeedbackStatus.New, Now.AddHours(1));

        result.Error.Code.ShouldBe("Feedback.StatusUnchanged");
        submission.StatusChangedAt.ShouldBeNull();
        submission.DomainEvents.OfType<FeedbackStatusChangedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void ChangeStatus_ToAnUndefinedValue_IsRefused()
        => Submit(Stars(2), null).ChangeStatus((FeedbackStatus)42, Now)
            .Error.Code.ShouldBe("Feedback.StatusInvalid");
}
