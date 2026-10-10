using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

// #2069: each reply is one mail and one row that ends in exactly one outcome. Nothing is ever resent
// on its own (Klas 2026-10-07); another attempt is a new reply the administrator writes.
public class FeedbackReplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static FeedbackSubmission Submission(JobSeekerId? owner = null) =>
        FeedbackSubmission.Submit(
            owner ?? new JobSeekerId(Guid.NewGuid()), Guid.NewGuid(), FeedbackPage.Jobs, FeedbackRating.Create(4).Value,
            null, ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null,
            Now.AddDays(-2)).Value;

    private static FeedbackReplyText Text(string text = "Tack för din feedback.") => FeedbackReplyText.Create(text).Value;

    private static FeedbackReply Sending() =>
        FeedbackReply.Start(Submission(), existingReplies: 0, Guid.NewGuid(), Text(), Now).Value;

    [Fact]
    public void Start_CopiesTheSubmissionsIdOwnerAndRetentionInstant()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        var submission = Submission(owner);
        var key = Guid.NewGuid();

        var reply = FeedbackReply.Start(submission, existingReplies: 0, key, Text("Hej"), Now).Value;

        reply.SubmissionId.ShouldBe(submission.Id);
        reply.JobSeekerId.ShouldBe(owner);
        reply.SubmittedAt.ShouldBe(submission.SubmittedAt);
        reply.ReplyKey.ShouldBe(key);
        reply.Text.Value.ShouldBe("Hej");
        reply.State.ShouldBe(FeedbackReplyState.Sending);
        reply.CreatedAt.ShouldBe(Now);
        reply.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Start_WithoutAKey_IsRefused()
    {
        var result = FeedbackReply.Start(Submission(), existingReplies: 0, Guid.Empty, Text(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.ReplyKeyRequired");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Start_BelowTheLimit_IsAccepted()
    {
        FeedbackReply.Start(Submission(), FeedbackReply.MaxPerSubmission - 1, Guid.NewGuid(), Text(), Now)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Start_AtTheLimit_IsAConflict()
    {
        var result = FeedbackReply.Start(Submission(), FeedbackReply.MaxPerSubmission, Guid.NewGuid(), Text(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Feedback.ReplyLimitReached");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void RecordAccepted_EndsTheSendAndStampsIt()
    {
        var reply = Sending();

        reply.RecordAccepted(Now.AddSeconds(2)).IsSuccess.ShouldBeTrue();

        reply.State.ShouldBe(FeedbackReplyState.Accepted);
        reply.CompletedAt.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public void RecordNotAccepted_IsFailedNotRequeued()
    {
        var reply = Sending();

        reply.RecordNotAccepted(Now.AddSeconds(2)).IsSuccess.ShouldBeTrue();

        reply.State.ShouldBe(FeedbackReplyState.Failed);
        reply.CompletedAt.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public void RecordUnknown_IsUnknown()
    {
        var reply = Sending();

        reply.RecordUnknown(Now.AddSeconds(30)).IsSuccess.ShouldBeTrue();

        reply.State.ShouldBe(FeedbackReplyState.Unknown);
        reply.CompletedAt.ShouldBe(Now.AddSeconds(30));
    }

    [Fact]
    public void AnOutcome_IsRecordedOnlyOnce()
    {
        var reply = Sending();
        reply.RecordAccepted(Now).IsSuccess.ShouldBeTrue();

        var again = reply.RecordUnknown(Now.AddSeconds(1));

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("Feedback.ReplyNotSending");
        again.Error.Kind.ShouldBe(ErrorKind.Conflict);
        reply.State.ShouldBe(FeedbackReplyState.Accepted);
        reply.RecordNotAccepted(Now).IsFailure.ShouldBeTrue();
        reply.RecordAccepted(Now).IsFailure.ShouldBeTrue();
    }

    // The actor that leaves a row in Sending is a process that died after scope 1 committed.
    [Fact]
    public void Effective_ASendingRowJustBeforeTheThreshold_IsStillSending()
    {
        var reply = Sending();

        FeedbackReply.Effective(reply.State, reply.CreatedAt, Now + FeedbackReply.StaleSendingAfter - TimeSpan.FromTicks(1))
            .ShouldBe(FeedbackReplyState.Sending);
    }

    [Fact]
    public void Effective_ASendingRowAtTheThreshold_IsUnknown()
    {
        var reply = Sending();

        FeedbackReply.Effective(reply.State, reply.CreatedAt, Now + FeedbackReply.StaleSendingAfter)
            .ShouldBe(FeedbackReplyState.Unknown);
        reply.State.ShouldBe(FeedbackReplyState.Sending);
    }

    [Theory]
    [InlineData(FeedbackReplyState.Accepted)]
    [InlineData(FeedbackReplyState.Failed)]
    [InlineData(FeedbackReplyState.Unknown)]
    public void Effective_ARecordedOutcome_NeverChangesWithTime(FeedbackReplyState state)
    {
        FeedbackReply.Effective(state, Now, Now.AddDays(1)).ShouldBe(state);
    }

    [Fact]
    public void StaleSendingAfter_IsWellPastTheProvidersTimeout()
    {
        FeedbackReply.StaleSendingAfter.ShouldBeGreaterThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ToString_NeverCarriesTheText()
    {
        var reply = FeedbackReply.Start(Submission(), 0, Guid.NewGuid(), Text("Hemligt svar"), Now).Value;

        reply.ToString().ShouldNotContain("Hemligt");
    }
}
