using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// #1979 — an administrator's new round of attempts for a submission's notice. From Failed nothing was sent, so no
/// acknowledgement is needed; from Unknown the earlier mail may have arrived, so the duplicate risk must be
/// acknowledged. Failed and Unknown are reached through <c>FeedbackNotificationDispatchJob</c>'s own transforms (the
/// claim, then the outcome it records), the order its unit tests pin.
/// </summary>
public sealed class RequeueFeedbackNotificationCommandHandlerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = T0.AddDays(1);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly JobSeekerId _owner = new(Guid.NewGuid());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task<FeedbackRows.Saved> SubmitAsync() =>
        FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);

    private async Task MoveAsync(FeedbackRows.Saved saved, Action<FeedbackNotification> transform)
    {
        var notice = await _db.FeedbackNotifications.SingleAsync(n => n.Id == saved.NoticeId, Ct);
        transform(notice);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
    }

    /// <summary>Five proven refusals, each claimed when due: the job ends the notice in Failed.</summary>
    private Task FailAsync(FeedbackRows.Saved saved) => MoveAsync(saved, notice =>
    {
        for (var attempt = 1; attempt <= FeedbackNotification.MaxAttempts; attempt++)
        {
            var due = notice.NextAttemptAt;
            notice.Claim(due).IsSuccess.ShouldBeTrue();
            notice.RecordNotAccepted(due).IsSuccess.ShouldBeTrue();
        }

        notice.State.ShouldBe(FeedbackNotificationState.Failed);
    });

    /// <summary>A send whose outcome the provider could not prove: the job records it Unknown.</summary>
    private Task LoseTheOutcomeAsync(FeedbackRows.Saved saved) => MoveAsync(saved, notice =>
    {
        notice.Claim(T0).IsSuccess.ShouldBeTrue();
        notice.RecordUnknown(T0).IsSuccess.ShouldBeTrue();
    });

    /// <summary>The handler's call, then the save <c>UnitOfWorkBehavior</c> makes after it.</summary>
    private async Task<Result> RequeueAsync(Guid submissionId, bool acknowledgeDuplicateRisk)
    {
        var result = await new RequeueFeedbackNotificationCommandHandler(_db, new FakeDateTimeProvider(Later))
            .Handle(new RequeueFeedbackNotificationCommand(submissionId, acknowledgeDuplicateRisk), Ct);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
        return result;
    }

    private Task<FeedbackNotification> NoticeAsync(FeedbackRows.Saved saved) =>
        _db.FeedbackNotifications.AsNoTracking().SingleAsync(n => n.Id == saved.NoticeId, Ct);

    private static void ShouldBeAFreshRound(FeedbackNotification notice)
    {
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.Attempts.ShouldBe(0);
        notice.NextAttemptAt.ShouldBe(Later);
        notice.SendingStartedAt.ShouldBeNull();
        notice.StateChangedAt.ShouldBe(Later);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AFailedNotice_StartsAFreshRoundWithOrWithoutTheAcknowledgement(bool acknowledged)
    {
        var saved = await SubmitAsync();
        await FailAsync(saved);

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledged);

        result.IsSuccess.ShouldBeTrue();
        ShouldBeAFreshRound(await NoticeAsync(saved));
    }

    [Fact]
    public async Task Handle_AnUnknownOutcomeWithoutTheAcknowledgement_IsRefusedAndStaysUnknown()
    {
        var saved = await SubmitAsync();
        await LoseTheOutcomeAsync(saved);

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("Feedback.DuplicateRiskNotAcknowledged");
        var notice = await NoticeAsync(saved);
        notice.State.ShouldBe(FeedbackNotificationState.Unknown);
        notice.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_AnUnknownOutcomeWithTheAcknowledgement_StartsAFreshRound()
    {
        var saved = await SubmitAsync();
        await LoseTheOutcomeAsync(saved);

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true);

        result.IsSuccess.ShouldBeTrue();
        ShouldBeAFreshRound(await NoticeAsync(saved));
    }

    [Fact]
    public async Task Handle_AQueuedNotice_IsNotRequeueable()
    {
        var saved = await SubmitAsync();

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("Feedback.NotificationNotRequeueable");
        var notice = await NoticeAsync(saved);
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.NextAttemptAt.ShouldBe(T0);
    }

    [Fact]
    public async Task Handle_AnAcceptedNotice_IsNotRequeueable()
    {
        var saved = await SubmitAsync();
        await MoveAsync(saved, notice =>
        {
            notice.Claim(T0).IsSuccess.ShouldBeTrue();
            notice.RecordAccepted(T0).IsSuccess.ShouldBeTrue();
        });

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true);

        result.Error.Code.ShouldBe("Feedback.NotificationNotRequeueable");
        (await NoticeAsync(saved)).State.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task Handle_AnUnknownSubmission_IsNotFound()
    {
        await SubmitAsync();

        var result = await RequeueAsync(Guid.NewGuid(), acknowledgeDuplicateRisk: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("Feedback.NotFound");
    }
}
