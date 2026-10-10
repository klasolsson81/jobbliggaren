using Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.SendFeedbackReply;

/// <summary>
/// #2069 — an administrator mails a reply to the person behind a feedback submission. The row is saved as Sending
/// with its audit before the send and records exactly one outcome after it. The provider's refusals are the two
/// dispositions <c>ScalewayEmailSender</c> itself sets: a 4xx is NotAccepted, a timeout or a 5xx stays Unknown.
/// </summary>
public sealed class SendFeedbackReplyCommandHandlerTests : IAsyncLifetime
{
    private const string ReporterEmail = "reporter@example.se";
    private const string Text = "Tack för din feedback. Vi tittar på det.";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = T0.AddDays(2);

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
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();

    private Func<Guid, string?> _emailOf;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SendFeedbackReplyCommandHandlerTests() =>
        _emailOf = userId => userId == _administrator.UserId ? "administrator@example.se"
            : userId == _reporter.UserId ? ReporterEmail : null;

    public async ValueTask InitializeAsync()
    {
        _db.JobSeekers.AddRange(_reporter, _administrator);
        _currentUser.UserId.Returns(_administrator.UserId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.AccessRevision.Returns(0L);
        _correlationId.Current.Returns(Guid.NewGuid());
        _sender.CanDeliver.Returns(true);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task<FeedbackRows.Saved> SubmitAsync(JobSeekerId? owner = null) =>
        FeedbackRows.SubmitAsync(_db, owner ?? _reporter.Id, FeedbackPage.Jobs, 3, "Filtret är svårt att hitta.", T0, Ct);

    private async Task<Result<FeedbackReplyOutcome>> ReplyAsync(
        Guid submissionId, Guid? key = null, string? text = Text, CancellationToken? cancellationToken = null)
    {
        var profiles = AccountAccessTestKit.ReaderFromProfiles(_db, _emailOf);
        var access = AccountAccessTestKit.Reader(userId =>
        {
            var account = profiles.ReadAsync(userId, Ct).GetAwaiter().GetResult();
            return userId == _administrator.UserId && account is not null ? account with { IsAdmin = true } : account;
        });
        var result = await new SendFeedbackReplyCommandHandler(_db, new FakeDateTimeProvider(Now), _currentUser,
                _coordinator, access, _sender, _correlationId, _requestContext,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SendFeedbackReplyCommandHandler>.Instance)
            .Handle(new SendFeedbackReplyCommand(submissionId, key ?? Guid.NewGuid(), text), cancellationToken ?? Ct);
        _coordinator.HasActiveScope.ShouldBeFalse();
        _db.ClearTracking();
        return result;
    }

    private Task<List<FeedbackReply>> RepliesAsync() => _db.FeedbackReplies.AsNoTracking().ToListAsync(Ct);

    private void ProviderRefuses(EmailDeliveryDisposition disposition) =>
        _sender.SendFeedbackReplyAsync(Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("feedback-reply", "HttpRequestException", disposition));

    [Fact]
    public async Task Handle_AnAcceptedSend_IsRecordedAndAuditedWithoutTheText()
    {
        var saved = await SubmitAsync();
        var auditBeforeTheSend = false;
        _coordinator.BeforeCommit = () =>
        {
            if (_coordinator.Commits == 0)
            {
                _coordinator.Holds(_administrator.UserId).ShouldBeTrue();
                _coordinator.Holds(_reporter.UserId).ShouldBeTrue();
                var audit = _db.AuditLogEntries.AsNoTracking().Single();
                audit.EventType.ShouldBe("Admin.FeedbackReplied");
                audit.AggregateId.ShouldBe(saved.SubmissionId.Value);
                audit.UserId.ShouldBe(_administrator.UserId);
                audit.Payload.ShouldBeNull();
                _db.FeedbackReplies.AsNoTracking().Single().State.ShouldBe(FeedbackReplyState.Sending);
                auditBeforeTheSend = true;
            }
        };

        var result = await ReplyAsync(saved.SubmissionId.Value);

        var outcome = result.Value;
        outcome.State.ShouldBe(FeedbackReplyState.Accepted);
        outcome.Replayed.ShouldBeFalse();
        auditBeforeTheSend.ShouldBeTrue();
        _coordinator.Commits.ShouldBe(2);
        _coordinator.BegunScopes[1].UserIds.ShouldBe([_reporter.UserId]);
        var reply = (await RepliesAsync()).ShouldHaveSingleItem();
        reply.Id.Value.ShouldBe(outcome.ReplyId);
        reply.State.ShouldBe(FeedbackReplyState.Accepted);
        reply.CompletedAt.ShouldBe(Now);
        reply.Text.Value.ShouldBe(Text);
        reply.SubmissionId.ShouldBe(saved.SubmissionId);
        reply.JobSeekerId.ShouldBe(_reporter.Id);
        reply.SubmittedAt.ShouldBe(T0);
        await _sender.Received(1).SendFeedbackReplyAsync(
            ReporterEmail,
            Arg.Is<FeedbackReplyEmail>(mail =>
                mail.Page == FeedbackPage.Jobs && mail.SubmittedAt == T0 && mail.Text.Value == Text),
            Arg.Any<CancellationToken>());
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(1);
    }

    [Theory]
    [InlineData(EmailDeliveryDisposition.NotAccepted, FeedbackReplyState.Failed)]
    [InlineData(EmailDeliveryDisposition.Unknown, FeedbackReplyState.Unknown)]
    public async Task Handle_AProviderRefusal_IsRecordedAsItsOutcomeAndNeverRetried(
        EmailDeliveryDisposition disposition, FeedbackReplyState expected)
    {
        var saved = await SubmitAsync();
        ProviderRefuses(disposition);

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe(expected);
        (await RepliesAsync()).ShouldHaveSingleItem().State.ShouldBe(expected);
        await _sender.Received(1).SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_TheSameKeyAgain_ReplaysTheOutcomeWithoutSendingTwice()
    {
        var saved = await SubmitAsync();
        var key = Guid.NewGuid();
        var first = (await ReplyAsync(saved.SubmissionId.Value, key)).Value;

        var again = (await ReplyAsync(saved.SubmissionId.Value, key)).Value;

        again.ShouldBe(first with { Replayed = true });
        (await RepliesAsync()).ShouldHaveSingleItem();
        await _sender.Received(1).SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_AFailedReplyThenANewKey_SendsANewReply()
    {
        var saved = await SubmitAsync();
        ProviderRefuses(EmailDeliveryDisposition.NotAccepted);
        await ReplyAsync(saved.SubmissionId.Value);
        _sender.SendFeedbackReplyAsync(Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var retry = (await ReplyAsync(saved.SubmissionId.Value)).Value;

        retry.State.ShouldBe(FeedbackReplyState.Accepted);
        (await RepliesAsync()).Select(r => r.State).Order()
            .ShouldBe([FeedbackReplyState.Accepted, FeedbackReplyState.Failed]);
    }

    [Fact]
    public async Task Handle_WhenNothingCanDeliver_RefusesBeforeSavingAnything()
    {
        var saved = await SubmitAsync();
        _sender.CanDeliver.Returns(false);

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.ReplyUnavailable");
        (await RepliesAsync()).ShouldBeEmpty();
        _coordinator.BegunScopes.ShouldBeEmpty();
        await _sender.DidNotReceive().SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Handle_NoText_IsRefusedBeforeSavingAnything(string? text)
    {
        var saved = await SubmitAsync();

        var result = await ReplyAsync(saved.SubmissionId.Value, text: text);

        result.Error.Code.ShouldBe("Feedback.ReplyEmpty");
        (await RepliesAsync()).ShouldBeEmpty();
        _coordinator.BegunScopes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AnUnknownSubmission_IsNotFound()
    {
        await SubmitAsync();

        var result = await ReplyAsync(Guid.NewGuid());

        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("Feedback.NotFound");
    }

    [Fact]
    public async Task Handle_AReporterWhoSoftDeletedTheirAccount_IsGoneAndNothingIsSent()
    {
        var saved = await SubmitAsync();
        var reporter = await _db.JobSeekers.SingleAsync(profile => profile.Id == _reporter.Id, Ct);
        reporter.SoftDelete(new FakeDateTimeProvider(Now));
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        result.Error.Code.ShouldBe("Feedback.ReporterUnavailable");
        _coordinator.Commits.ShouldBe(0);
        (await RepliesAsync()).ShouldBeEmpty();
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        await _sender.DidNotReceive().SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    // The access reader answers null for an Identity user that no longer exists.
    [Fact]
    public async Task Handle_AReporterWhoseAccountIsGone_IsGone()
    {
        var saved = await SubmitAsync();
        _emailOf = userId => userId == _administrator.UserId ? "administrator@example.se" : null;

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Error.Code.ShouldBe("Feedback.ReporterUnavailable");
        (await RepliesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_TheEleventhReply_IsAConflict()
    {
        var saved = await SubmitAsync();
        for (var i = 0; i < FeedbackReply.MaxPerSubmission; i++)
            (await ReplyAsync(saved.SubmissionId.Value)).IsSuccess.ShouldBeTrue();

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("Feedback.ReplyLimitReached");
        (await RepliesAsync()).Count.ShouldBe(FeedbackReply.MaxPerSubmission);
    }

    // Klas's first live test answers his own feedback: one account is both the actor and the reporter.
    [Fact]
    public async Task Handle_AnAdministratorAnsweringTheirOwnFeedback_Sends()
    {
        var saved = await SubmitAsync(_administrator.Id);

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Value.State.ShouldBe(FeedbackReplyState.Accepted);
        await _sender.Received(1).SendFeedbackReplyAsync(
            "administrator@example.se", Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    // After scope 1 commits, an administrator who closes the tab must not leave the reply recorded as still sending.
    [Fact]
    public async Task Handle_TheRequestCancelledDuringTheSend_StillRecordsTheOutcome()
    {
        var saved = await SubmitAsync();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _sender.SendFeedbackReplyAsync(Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().CanBeCanceled.ShouldBeFalse();
                request.Cancel();
                return Task.CompletedTask;
            });

        var result = await ReplyAsync(saved.SubmissionId.Value, cancellationToken: request.Token);

        result.Value.State.ShouldBe(FeedbackReplyState.Accepted);
        (await RepliesAsync()).ShouldHaveSingleItem().State.ShouldBe(FeedbackReplyState.Accepted);
    }

    [Fact]
    public async Task Handle_TheReplyRemovedDuringTheSend_IsGone()
    {
        var cutoff = Now - FeedbackRetentionJob.Retention;
        var saved = await FeedbackRows.SubmitAsync(
            _db, _reporter.Id, FeedbackPage.Jobs, 3, "Filtret är svårt att hitta.", cutoff.AddMinutes(-1), Ct);
        _sender.SendFeedbackReplyAsync(Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _db.FeedbackReplies.RemoveRange(_db.FeedbackReplies.Where(r => r.SubmittedAt < cutoff));
                _db.SaveChanges();
                _db.ClearTracking();
                return Task.CompletedTask;
            });

        var result = await ReplyAsync(saved.SubmissionId.Value);

        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        result.Error.Code.ShouldBe("Feedback.ReplyRemoved");
        (await RepliesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AnAdministratorWhoseProfileWasSoftDeleted_IsRefusedAndNothingIsSent()
    {
        var saved = await SubmitAsync();
        var actor = await _db.JobSeekers.SingleAsync(profile => profile.Id == _administrator.Id, Ct);
        actor.SoftDelete(new FakeDateTimeProvider(Now));
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        await Should.ThrowAsync<ReauthenticationFailedException>(async () => await ReplyAsync(saved.SubmissionId.Value));

        _coordinator.HasActiveScope.ShouldBeFalse();
        _coordinator.Commits.ShouldBe(0);
        (await RepliesAsync()).ShouldBeEmpty();
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        await _sender.DidNotReceive().SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameKeyAfterTheReporterLeft_StillReplaysTheRecordedOutcome()
    {
        var saved = await SubmitAsync();
        var key = Guid.NewGuid();
        var first = (await ReplyAsync(saved.SubmissionId.Value, key)).Value;
        var reporter = await _db.JobSeekers.SingleAsync(profile => profile.Id == _reporter.Id, Ct);
        reporter.SoftDelete(new FakeDateTimeProvider(Now));
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var again = await ReplyAsync(saved.SubmissionId.Value, key);

        again.Value.ShouldBe(first with { Replayed = true });
        await _sender.Received(1).SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    // A row FeedbackReply.Start saved in scope 1 of a request that died before scope 2.
    [Fact]
    public async Task Handle_TheSameKeyAsAStaleSend_ReplaysItAsUnknownWithoutSending()
    {
        var saved = await SubmitAsync();
        var submission = await _db.FeedbackSubmissions.AsNoTracking().SingleAsync(s => s.Id == saved.SubmissionId, Ct);
        var key = Guid.NewGuid();
        var stale = FeedbackReply.Start(
            submission, 0, key, FeedbackReplyText.Create(Text).Value, Now - FeedbackReply.StaleSendingAfter).Value;
        _db.FeedbackReplies.Add(stale);
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var replayed = (await ReplyAsync(saved.SubmissionId.Value, key)).Value;

        replayed.ShouldBe(new FeedbackReplyOutcome(stale.Id.Value, FeedbackReplyState.Unknown, true));
        await _sender.DidNotReceive().SendFeedbackReplyAsync(
            Arg.Any<string>(), Arg.Any<FeedbackReplyEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_OwnsItsAuditedAdminTransaction_AndNeverPrintsTheText()
    {
        var id = Guid.NewGuid();
        var command = new SendFeedbackReplyCommand(id, Guid.NewGuid(), "Hemligt svar");

        command.ShouldBeAssignableTo<IAdminRequest>();
        command.ShouldBeAssignableTo<IOwnsAccountTransaction>();
        ((object)command is IReplayOnConcurrencyConflict).ShouldBeFalse();
        var audited = command.ShouldBeAssignableTo<IAuditableCommand<Result<FeedbackReplyOutcome>>>();
        audited.EventType.ShouldBe("Admin.FeedbackReplied");
        audited.AggregateType.ShouldBe("Feedback");
        command.ToString().ShouldNotContain("Hemligt");
    }
}
