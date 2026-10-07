using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Feedback;

// Klas 2026-10-07: an unknown outcome is never resent without an administrator acknowledging that the
// mail may arrive twice.
public class FeedbackNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static FeedbackNotification Queued() =>
        FeedbackNotification.QueueFor(FeedbackSubmission.Submit(
            new JobSeekerId(Guid.NewGuid()), Guid.NewGuid(), FeedbackPage.Jobs, FeedbackRating.Create(4).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null, Now).Value);

    private static FeedbackNotification Sending()
    {
        var notification = Queued();
        notification.Claim(Now).IsSuccess.ShouldBeTrue();
        return notification;
    }

    [Fact]
    public void QueueFor_IsDueAtOnce()
    {
        var notification = Queued();

        notification.State.ShouldBe(FeedbackNotificationState.Queued);
        notification.Attempts.ShouldBe(0);
        notification.NextAttemptAt.ShouldBe(Now);
        notification.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void QueueFor_TakesTheSubmissionsIdOwnerAndTime()
    {
        var owner = new JobSeekerId(Guid.NewGuid());
        var submission = FeedbackSubmission.Submit(
            owner, Guid.NewGuid(), FeedbackPage.Matches, null, FeedbackComment.Create("Bra sida.").Value,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null,
            Now.AddMinutes(-3)).Value;

        var notification = FeedbackNotification.QueueFor(submission);

        notification.SubmissionId.ShouldBe(submission.Id);
        notification.JobSeekerId.ShouldBe(owner);
        notification.CreatedAt.ShouldBe(submission.SubmittedAt);
        notification.NextAttemptAt.ShouldBe(submission.SubmittedAt);
    }

    [Fact]
    public void Claim_WhenDue_MarksSendingAndCountsTheAttempt()
    {
        var notification = Queued();

        notification.Claim(Now).IsSuccess.ShouldBeTrue();

        notification.State.ShouldBe(FeedbackNotificationState.Sending);
        notification.Attempts.ShouldBe(1);
        notification.SendingStartedAt.ShouldBe(Now);
    }

    [Fact]
    public void Claim_BeforeItIsDue_IsRefused()
    {
        var notification = Sending();
        notification.RecordNotAccepted(Now).IsSuccess.ShouldBeTrue();

        notification.Claim(Now.AddSeconds(30)).Error.Code.ShouldBe("Feedback.NotificationNotDue");
    }

    [Fact]
    public void RecordAccepted_IsFinal()
    {
        var notification = Sending();

        notification.RecordAccepted(Now.AddSeconds(2)).IsSuccess.ShouldBeTrue();

        notification.State.ShouldBe(FeedbackNotificationState.Accepted);
        notification.AcceptedAt.ShouldBe(Now.AddSeconds(2));
        notification.Claim(Now.AddDays(1)).IsFailure.ShouldBeTrue();
        notification.Requeue(acknowledgeDuplicateRisk: true, Now.AddDays(1)).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    public void RecordNotAccepted_BeforeTheLastAttempt_RequeuesWithBackoff(int failedAttempts, int waitMinutes)
    {
        var notification = Queued();
        var at = Now;
        for (var attempt = 1; attempt <= failedAttempts; attempt++)
        {
            at = notification.NextAttemptAt;
            notification.Claim(at).IsSuccess.ShouldBeTrue();
            notification.RecordNotAccepted(at).IsSuccess.ShouldBeTrue();
        }

        notification.State.ShouldBe(FeedbackNotificationState.Queued);
        notification.Attempts.ShouldBe(failedAttempts);
        notification.NextAttemptAt.ShouldBe(at.AddMinutes(waitMinutes));
    }

    [Fact]
    public void RecordNotAccepted_OnTheFifthAttempt_Fails()
    {
        var notification = Queued();
        for (var attempt = 1; attempt <= FeedbackNotification.MaxAttempts; attempt++)
        {
            notification.Claim(notification.NextAttemptAt).IsSuccess.ShouldBeTrue();
            notification.RecordNotAccepted(notification.NextAttemptAt).IsSuccess.ShouldBeTrue();
        }

        notification.State.ShouldBe(FeedbackNotificationState.Failed);
        notification.Attempts.ShouldBe(FeedbackNotification.MaxAttempts);
        notification.Claim(Now.AddDays(1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void RecordUnknown_IsNeverClaimedAgainOnItsOwn()
    {
        var notification = Sending();

        notification.RecordUnknown(Now).IsSuccess.ShouldBeTrue();

        notification.State.ShouldBe(FeedbackNotificationState.Unknown);
        notification.Claim(Now.AddDays(30)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Outcomes_WithoutAClaim_AreRefused()
    {
        var notification = Queued();

        notification.RecordAccepted(Now).IsFailure.ShouldBeTrue();
        notification.RecordNotAccepted(Now).IsFailure.ShouldBeTrue();
        notification.RecordUnknown(Now).IsFailure.ShouldBeTrue();
        notification.State.ShouldBe(FeedbackNotificationState.Queued);
    }

    [Fact]
    public void ExpireIfStale_ASendThatOutlivedItsWindow_BecomesUnknownNotQueued()
    {
        var notification = Sending();

        notification.ExpireIfStale(Now.Add(FeedbackNotification.StaleSendingAfter)).ShouldBeTrue();

        notification.State.ShouldBe(FeedbackNotificationState.Unknown);
    }

    [Fact]
    public void ExpireIfStale_ASendStillInsideItsWindow_IsLeftAlone()
    {
        var notification = Sending();

        notification.ExpireIfStale(Now.Add(FeedbackNotification.StaleSendingAfter).AddSeconds(-1)).ShouldBeFalse();

        notification.State.ShouldBe(FeedbackNotificationState.Sending);
    }

    [Fact]
    public void Requeue_AFailedNotice_StartsAFreshRoundWithoutAnAcknowledgement()
    {
        var notification = Queued();
        for (var attempt = 1; attempt <= FeedbackNotification.MaxAttempts; attempt++)
        {
            notification.Claim(notification.NextAttemptAt).IsSuccess.ShouldBeTrue();
            notification.RecordNotAccepted(notification.NextAttemptAt).IsSuccess.ShouldBeTrue();
        }
        var later = Now.AddDays(1);

        notification.Requeue(acknowledgeDuplicateRisk: false, later).IsSuccess.ShouldBeTrue();

        notification.State.ShouldBe(FeedbackNotificationState.Queued);
        notification.Attempts.ShouldBe(0);
        notification.NextAttemptAt.ShouldBe(later);
    }

    [Fact]
    public void Requeue_AnUnknownOutcome_NeedsTheDuplicateRiskAcknowledged()
    {
        var notification = Sending();
        notification.RecordUnknown(Now).IsSuccess.ShouldBeTrue();

        notification.Requeue(acknowledgeDuplicateRisk: false, Now).Error.Code
            .ShouldBe("Feedback.DuplicateRiskNotAcknowledged");
        notification.State.ShouldBe(FeedbackNotificationState.Unknown);

        notification.Requeue(acknowledgeDuplicateRisk: true, Now).IsSuccess.ShouldBeTrue();
        notification.State.ShouldBe(FeedbackNotificationState.Queued);
        notification.Attempts.ShouldBe(0);
    }

    [Fact]
    public void Requeue_AQueuedOrSendingNotice_IsRefused()
    {
        Queued().Requeue(true, Now).Error.Code.ShouldBe("Feedback.NotificationNotRequeueable");
        Sending().Requeue(true, Now).Error.Code.ShouldBe("Feedback.NotificationNotRequeueable");
    }
}
