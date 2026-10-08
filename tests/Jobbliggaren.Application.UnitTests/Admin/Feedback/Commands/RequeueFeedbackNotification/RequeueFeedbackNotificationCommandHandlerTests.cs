using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.RequeueFeedbackNotification;

/// <summary>
/// #1979 — an administrator's new round of attempts for a submission's notice. From Failed nothing was sent, so no
/// acknowledgement is needed; from Unknown the earlier mail may have arrived, so the duplicate risk must be
/// acknowledged. Failed and Unknown are reached through <c>FeedbackNotificationDispatchJob</c>'s own transforms (the
/// claim, then the outcome it records), the order its unit tests pin.
/// </summary>
public sealed class RequeueFeedbackNotificationCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = T0.AddDays(1);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private static readonly FakeDateTimeProvider RegistrationClock = new(T0);
    private readonly JobSeeker _reporter = JobSeeker.Register(
        Guid.NewGuid(), TermsAcceptance.AcceptCurrent(RegistrationClock), RegistrationClock).Value;
    private readonly JobSeeker _administrator = JobSeeker.Register(
        Guid.NewGuid(), TermsAcceptance.AcceptCurrent(RegistrationClock), RegistrationClock).Value;
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICorrelationIdProvider _correlationId = Substitute.For<ICorrelationIdProvider>();
    private readonly IRequestContextProvider _requestContext = Substitute.For<IRequestContextProvider>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _db.JobSeekers.AddRange(_reporter, _administrator);
        _currentUser.UserId.Returns(_administrator.UserId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.AccessRevision.Returns(0L);
        _correlationId.Current.Returns(Guid.NewGuid());
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task<FeedbackRows.Saved> SubmitAsync() =>
        FeedbackRows.SubmitAsync(_db, _reporter.Id, FeedbackPage.Jobs, 3, null, T0, Ct);

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

    /// <summary>The recording scope witnesses ownership calls; PostgreSQL tests witness atomic rollback.</summary>
    private async Task<Result> RequeueAsync(Guid submissionId, bool acknowledgeDuplicateRisk)
    {
        var profiles = AccountAccessTestKit.ReaderFromProfiles(_db, userId =>
            userId == _administrator.UserId ? "administrator@example.se" : null);
        var actor = (await profiles.ReadAsync(_administrator.UserId, Ct)).ShouldNotBeNull() with { IsAdmin = true };
        var access = AccountAccessTestKit.Reader(userId => userId == _administrator.UserId ? actor : null);
        var result = await new RequeueFeedbackNotificationCommandHandler(_db, new FakeDateTimeProvider(Later),
                _currentUser, _coordinator, access, _correlationId, _requestContext)
            .Handle(new RequeueFeedbackNotificationCommand(submissionId, acknowledgeDuplicateRisk), Ct);
        _coordinator.HasActiveScope.ShouldBeFalse();
        if (result.IsFailure)
        {
            _coordinator.Commits.ShouldBe(0);
            (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        }
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

    [Fact]
    public async Task Handle_ShouldSaveOneSuccessAuditBeforeOwnedCommit_WhenTheReporterIsLive()
    {
        var saved = await SubmitAsync();
        await LoseTheOutcomeAsync(saved);
        var commitObserved = false;
        _coordinator.BeforeCommit = () =>
        {
            _coordinator.Holds(_administrator.UserId).ShouldBeTrue();
            _coordinator.Holds(_reporter.UserId).ShouldBeTrue();
            var audit = _db.AuditLogEntries.AsNoTracking().Single();
            audit.EventType.ShouldBe("Admin.FeedbackNotificationRequeued");
            audit.AggregateId.ShouldBe(saved.SubmissionId.Value);
            audit.UserId.ShouldBe(_administrator.UserId);
            commitObserved = true;
        };

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true);

        result.IsSuccess.ShouldBeTrue();
        commitObserved.ShouldBeTrue();
        _coordinator.Commits.ShouldBe(1);
        var begun = _coordinator.BegunScopes.ShouldHaveSingleItem();
        begun.Lifecycle.ShouldBeFalse();
        begun.UserIds.Order().ShouldBe(new[] { _administrator.UserId, _reporter.UserId }.Distinct().Order());
        ShouldBeAFreshRound(await NoticeAsync(saved));
    }

    [Fact]
    public async Task Handle_ShouldRefuseWithoutAuditOrCommit_WhenTheActorProfileWasActuallySoftDeleted()
    {
        var saved = await SubmitAsync();
        await LoseTheOutcomeAsync(saved);
        var actor = await _db.JobSeekers.SingleAsync(profile => profile.Id == _administrator.Id, Ct);
        actor.SoftDelete(new FakeDateTimeProvider(Later));
        actor.DeletedAt.ShouldBe(Later);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        await Should.ThrowAsync<ReauthenticationFailedException>(async () =>
            await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true));

        _coordinator.HasActiveScope.ShouldBeFalse();
        _coordinator.Commits.ShouldBe(0);
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        (await NoticeAsync(saved)).State.ShouldBe(FeedbackNotificationState.Unknown);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ShouldRefuseWithoutChangingTheNotice_WhenTheReporterIsPendingDeletion(bool unknownOutcome)
    {
        var saved = await SubmitAsync();
        if (unknownOutcome)
            await LoseTheOutcomeAsync(saved);
        else
            await FailAsync(saved);
        var before = await NoticeAsync(saved);
        var reporter = await _db.JobSeekers.SingleAsync(value => value.Id == _reporter.Id, Ct);
        reporter.SoftDelete(new FakeDateTimeProvider(Later));
        reporter.DeletedAt.ShouldBe(Later);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var result = await RequeueAsync(saved.SubmissionId.Value, acknowledgeDuplicateRisk: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        result.Error.Code.ShouldBe("Feedback.ReporterUnavailable");
        var after = await NoticeAsync(saved);
        after.State.ShouldBe(before.State);
        after.Attempts.ShouldBe(before.Attempts);
        after.NextAttemptAt.ShouldBe(before.NextAttemptAt);
        after.SendingStartedAt.ShouldBe(before.SendingStartedAt);
        after.AcceptedAt.ShouldBe(before.AcceptedAt);
        after.StateChangedAt.ShouldBe(before.StateChangedAt);
    }
}
