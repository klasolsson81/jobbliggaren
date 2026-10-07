using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Sessions;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback.Jobs.DispatchFeedbackNotifications;

/// <summary>
/// #1979 — the operator notices. Each notice is saved Sending before the provider call; a refusal the provider proves
/// is retried with backoff up to five attempts; an outcome that may have been accepted is never sent again on its own;
/// the run stops at the first send that is not accepted. Every state below is produced by the job itself — a claim, a
/// refusal, a run interrupted by the host's shutdown token — never set by hand. Each run gets a fresh change tracker,
/// as each Hangfire run gets a fresh scope. The xmin claim guard needs Postgres and is proven in
/// <c>FeedbackNotificationDispatchJobIntegrationTests</c> (Worker).
/// </summary>
public sealed class FeedbackNotificationDispatchJobTests : IAsyncDisposable
{
    private const string Recipient = "feedback-operator@example.test";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly FeedbackOptions Open = new() { Enabled = true, NotificationRecipient = Recipient };

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = T0 };
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly RecordingLogger<FeedbackNotificationDispatchJob> _log = new();
    private readonly JobSeekerId _owner = new(Guid.NewGuid());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FeedbackNotificationDispatchJobTests() => _sender.CanDeliver.Returns(true);

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private FeedbackNotificationDispatchJob Job(AppDbContext db, FeedbackOptions? options = null) =>
        new(db, _sender, new FeedbackGate(Options.Create(options ?? Open), _sender), _clock, _log);

    /// <summary>One scheduled run, on a fresh change tracker like the fresh scope each Hangfire run resolves.</summary>
    private async Task RunAsync(AppDbContext db, FeedbackOptions? options = null)
    {
        db.ClearTracking();
        await Job(db, options).RunAsync(Ct);
    }

    private Task<FeedbackRows.Saved> SubmitAsync(DateTimeOffset at, FeedbackPage? page = null, int? rating = 3) =>
        FeedbackRows.SubmitAsync(_db, _owner, page ?? FeedbackPage.Jobs, rating, null, at, Ct);

    private static Task<FeedbackNotification> NoticeAsync(AppDbContext db, FeedbackNotificationId id) =>
        db.FeedbackNotifications.AsNoTracking().SingleAsync(n => n.Id == id, Ct);

    private int SendCount() =>
        _sender.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(IEmailSender.SendFeedbackReceivedNotificationAsync));

    private void ProviderAccepts() =>
        _sender.SendFeedbackReceivedNotificationAsync(
                Arg.Any<string>(), Arg.Any<FeedbackReceivedNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

    private void ProviderFails(EmailDeliveryDisposition disposition) =>
        _sender.SendFeedbackReceivedNotificationAsync(
                Arg.Any<string>(), Arg.Any<FeedbackReceivedNotificationEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("feedback-received-notification", "HttpRequestException", disposition));

    /// <summary>
    /// A run the host stopped during the provider call: the claim was saved, and the cancellation the run asked for
    /// escapes the transport (<c>ScalewayEmailSender</c> contains every failure except that one). The notice is left
    /// Sending, which is what the next runs must deal with.
    /// </summary>
    private async Task StopTheHostDuringTheSendAsync(AppDbContext db)
    {
        using var shutdown = new CancellationTokenSource();
        _sender.SendFeedbackReceivedNotificationAsync(
                Arg.Any<string>(), Arg.Any<FeedbackReceivedNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                shutdown.Cancel();
                return Task.FromException(new OperationCanceledException(shutdown.Token));
            });

        await Should.ThrowAsync<OperationCanceledException>(() => Job(db).RunAsync(shutdown.Token));
        ProviderAccepts();
    }

    [Fact]
    public async Task RunAsync_ADueNotice_IsSentOnceToTheRecipientAndRecordedAccepted()
    {
        var saved = await SubmitAsync(T0, FeedbackPage.Jobs, rating: 4);
        _clock.UtcNow = T0.AddSeconds(30);

        await RunAsync(_db);

        await _sender.Received(1).SendFeedbackReceivedNotificationAsync(
            Recipient,
            Arg.Is<FeedbackReceivedNotificationEmail>(content => content != null
                && content.Page == FeedbackPage.Jobs
                && content.Rating == 4
                && content.SubmittedAt == T0
                && content.FeedbackId == saved.SubmissionId.Value),
            Arg.Any<CancellationToken>());
        var notice = await NoticeAsync(_db, saved.NoticeId);
        notice.State.ShouldBe(FeedbackNotificationState.Accepted);
        notice.Attempts.ShouldBe(1);
        notice.AcceptedAt.ShouldBe(T0.AddSeconds(30));

        _clock.UtcNow = T0.AddMinutes(5);
        await RunAsync(_db);

        SendCount().ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_TextOnlyFeedback_IsSentWithoutARating()
    {
        var saved = await FeedbackRows.SubmitAsync(_db, _owner, FeedbackPage.Cv, null, "Texten är för liten.", T0, Ct);

        await RunAsync(_db);

        await _sender.Received(1).SendFeedbackReceivedNotificationAsync(
            Recipient,
            Arg.Is<FeedbackReceivedNotificationEmail>(content => content != null
                && content.Rating == null
                && content.Page == FeedbackPage.Cv
                && content.FeedbackId == saved.SubmissionId.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AProvenRefusal_RequeuesWithBackoffAndStopsTheRun()
    {
        var oldest = await SubmitAsync(T0);
        var next = await SubmitAsync(T0.AddSeconds(1));
        ProviderFails(EmailDeliveryDisposition.NotAccepted);
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db);

        SendCount().ShouldBe(1);
        await _sender.Received(1).SendFeedbackReceivedNotificationAsync(
            Recipient,
            Arg.Is<FeedbackReceivedNotificationEmail>(content => content != null
                && content.FeedbackId == oldest.SubmissionId.Value),
            Arg.Any<CancellationToken>());
        var refused = await NoticeAsync(_db, oldest.NoticeId);
        refused.State.ShouldBe(FeedbackNotificationState.Queued);
        refused.Attempts.ShouldBe(1);
        refused.NextAttemptAt.ShouldBe(T0.AddMinutes(2));
        var untouched = await NoticeAsync(_db, next.NoticeId);
        untouched.State.ShouldBe(FeedbackNotificationState.Queued);
        untouched.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_FiveProvenRefusals_EndInFailedAfterExactlyFiveSends()
    {
        var saved = await SubmitAsync(T0);
        ProviderFails(EmailDeliveryDisposition.NotAccepted);
        TimeSpan[] backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

        for (var attempt = 1; attempt <= FeedbackNotification.MaxAttempts; attempt++)
        {
            var due = (await NoticeAsync(_db, saved.NoticeId)).NextAttemptAt;

            _clock.UtcNow = due.AddSeconds(-1);
            await RunAsync(_db);
            SendCount().ShouldBe(attempt - 1, "a notice is never sent before its backoff has run out");

            _clock.UtcNow = due;
            await RunAsync(_db);
            SendCount().ShouldBe(attempt);

            var after = await NoticeAsync(_db, saved.NoticeId);
            after.Attempts.ShouldBe(attempt);
            if (attempt < FeedbackNotification.MaxAttempts)
            {
                after.State.ShouldBe(FeedbackNotificationState.Queued);
                (after.NextAttemptAt - due).ShouldBe(backoff[attempt - 1]);
            }
        }

        (await NoticeAsync(_db, saved.NoticeId)).State.ShouldBe(FeedbackNotificationState.Failed);

        _clock.UtcNow = T0.AddDays(2);
        await RunAsync(_db);

        SendCount().ShouldBe(FeedbackNotification.MaxAttempts);
    }

    [Fact]
    public async Task RunAsync_AnUnknownOutcome_IsNeverSentAgainAndStopsTheRun()
    {
        var unknown = await SubmitAsync(T0);
        var next = await SubmitAsync(T0.AddSeconds(1));
        ProviderFails(EmailDeliveryDisposition.Unknown);
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db);

        SendCount().ShouldBe(1);
        var recorded = await NoticeAsync(_db, unknown.NoticeId);
        recorded.State.ShouldBe(FeedbackNotificationState.Unknown);
        recorded.Attempts.ShouldBe(1);
        (await NoticeAsync(_db, next.NoticeId)).Attempts.ShouldBe(0);

        ProviderAccepts();
        _clock.UtcNow = T0.AddDays(1);
        await RunAsync(_db);
        _clock.UtcNow = T0.AddDays(2);
        await RunAsync(_db);

        SendCount().ShouldBe(2, "only the notice behind it went out");
        await _sender.Received(1).SendFeedbackReceivedNotificationAsync(
            Recipient,
            Arg.Is<FeedbackReceivedNotificationEmail>(content => content != null
                && content.FeedbackId == unknown.SubmissionId.Value),
            Arg.Any<CancellationToken>());
        (await NoticeAsync(_db, unknown.NoticeId)).State.ShouldBe(FeedbackNotificationState.Unknown);
        (await NoticeAsync(_db, next.NoticeId)).State.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task RunAsync_ASendingNoticeLeftByAStoppedRun_BecomesUnknownAfterTenMinutesAndIsNeverResent()
    {
        var saved = await SubmitAsync(T0);
        await StopTheHostDuringTheSendAsync(_db);

        var stuck = await NoticeAsync(_db, saved.NoticeId);
        stuck.State.ShouldBe(FeedbackNotificationState.Sending);
        stuck.Attempts.ShouldBe(1);
        stuck.SendingStartedAt.ShouldBe(T0);

        _clock.UtcNow = T0 + FeedbackNotification.StaleSendingAfter - TimeSpan.FromSeconds(1);
        await RunAsync(_db);

        (await NoticeAsync(_db, saved.NoticeId)).State.ShouldBe(FeedbackNotificationState.Sending);

        _clock.UtcNow = T0 + FeedbackNotification.StaleSendingAfter;
        await RunAsync(_db);

        var expired = await NoticeAsync(_db, saved.NoticeId);
        expired.State.ShouldBe(FeedbackNotificationState.Unknown);
        expired.Attempts.ShouldBe(1);
        expired.StateChangedAt.ShouldBe(T0 + FeedbackNotification.StaleSendingAfter);

        _clock.UtcNow = T0.AddDays(1);
        await RunAsync(_db);

        SendCount().ShouldBe(1, "only the call the stopped run made");
    }

    [Fact]
    public async Task RunAsync_WithoutADeliverableRecipient_StillExpiresAStaleSend()
    {
        var saved = await SubmitAsync(T0);
        await StopTheHostDuringTheSendAsync(_db);
        _clock.UtcNow = T0 + FeedbackNotification.StaleSendingAfter;

        await RunAsync(_db, new FeedbackOptions { Enabled = true, NotificationRecipient = null });

        (await NoticeAsync(_db, saved.NoticeId)).State.ShouldBe(FeedbackNotificationState.Unknown);
        SendCount().ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_AQueuedNoticeLeftByARunStoppedBeforeItsClaimWasSaved_IsSentOnceByTheNextRun()
    {
        var shutdownAtTheClaim = new ShutdownDuringTheClaimSave();
        await using var db = TestAppDbContextFactory.Create(shutdownAtTheClaim);
        var saved = await FeedbackRows.SubmitAsync(db, _owner, FeedbackPage.Jobs, 3, null, T0, Ct);

        await Should.ThrowAsync<OperationCanceledException>(() => Job(db).RunAsync(Ct));

        shutdownAtTheClaim.Fired.ShouldBe(1);
        SendCount().ShouldBe(0);
        var left = await NoticeAsync(db, saved.NoticeId);
        left.State.ShouldBe(FeedbackNotificationState.Queued);
        left.Attempts.ShouldBe(0);

        _clock.UtcNow = T0.AddMinutes(1);
        await RunAsync(db);

        SendCount().ShouldBe(1);
        var sent = await NoticeAsync(db, saved.NoticeId);
        sent.State.ShouldBe(FeedbackNotificationState.Accepted);
        sent.Attempts.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("not-an-address", true)]
    [InlineData(Recipient, false)]
    public async Task RunAsync_WithoutADeliverableRecipient_ClaimsAndSendsNothing(string? recipient, bool canDeliver)
    {
        _sender.CanDeliver.Returns(canDeliver);
        var saved = await SubmitAsync(T0);
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db, new FeedbackOptions { Enabled = true, NotificationRecipient = recipient });

        SendCount().ShouldBe(0);
        var notice = await NoticeAsync(_db, saved.NoticeId);
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.Attempts.ShouldBe(0);
        notice.SendingStartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_SwitchedOffAfterTheNoticeWasQueued_StillSendsIt()
    {
        var saved = await SubmitAsync(T0);

        await RunAsync(_db, new FeedbackOptions { Enabled = false, NotificationRecipient = Recipient });

        SendCount().ShouldBe(1);
        (await NoticeAsync(_db, saved.NoticeId)).State.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task RunAsync_ANoticeWhoseSubmissionIsGone_IsRemovedWithoutASendAndDoesNotHoldUpTheQueue()
    {
        // DECLARED UNREACHABLE AT REST (CLAUDE.md §5 Tests:): every writer removes a notice before its submission or
        // with it in one transaction — FeedbackRetentionJob deletes notices first, AccountHardDeleter deletes both in
        // one transaction — so no path in src/ leaves this orphan behind. The branch exists for a deletion between
        // the job's two reads. Asserted only as the read side's safe degradation if that invariant breaks: the orphan
        // is removed, nothing is sent for it, and the notice behind it still goes out.
        var orphan = FeedbackSubmission.Submit(_owner, Guid.NewGuid(), FeedbackPage.Jobs, null,
            FeedbackComment.Create("Utan sin inskickning.").Value, FeedbackRows.NoClient(), null, T0).Value;
        var orphanNotice = FeedbackNotification.QueueFor(orphan);
        _db.FeedbackNotifications.Add(orphanNotice);
        await _db.SaveChangesAsync(Ct);
        var behind = await SubmitAsync(T0.AddSeconds(1));
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db);

        (await _db.FeedbackNotifications.AsNoTracking().AnyAsync(n => n.Id == orphanNotice.Id, Ct)).ShouldBeFalse();
        await _sender.DidNotReceive().SendFeedbackReceivedNotificationAsync(
            Arg.Any<string>(),
            Arg.Is<FeedbackReceivedNotificationEmail>(content => content != null && content.FeedbackId == orphan.Id.Value),
            Arg.Any<CancellationToken>());
        SendCount().ShouldBe(1);
        (await NoticeAsync(_db, behind.NoticeId)).State.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task RunAsync_MoreDueNoticesThanOneRunMaySend_SendsTheOldestAndLeavesTheRestForTheNextRun()
    {
        var saved = new List<FeedbackRows.Saved>();
        for (var i = 0; i < FeedbackNotificationDispatchJob.MaxSendsPerRun + 2; i++)
            saved.Add(await SubmitAsync(T0.AddSeconds(i)));
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db);

        SendCount().ShouldBe(FeedbackNotificationDispatchJob.MaxSendsPerRun);
        for (var i = 0; i < saved.Count; i++)
        {
            (await NoticeAsync(_db, saved[i].NoticeId)).State.ShouldBe(
                i < FeedbackNotificationDispatchJob.MaxSendsPerRun
                    ? FeedbackNotificationState.Accepted
                    : FeedbackNotificationState.Queued);
        }

        await RunAsync(_db);

        SendCount().ShouldBe(FeedbackNotificationDispatchJob.MaxSendsPerRun + 2);
    }

    [Fact]
    public async Task RunAsync_WithoutARecipient_WarnsOnlyWhileANoticeWaits_AndNamesTheMissingRecipient()
    {
        var closed = new FeedbackOptions { Enabled = false, NotificationRecipient = null };

        await RunAsync(_db, closed);

        _log.Records.ShouldNotContain(record => record.EventId.Id == 3101);

        // Queued while a recipient was configured; the configuration lost it afterwards.
        await SubmitAsync(T0);
        _clock.UtcNow = T0.AddMinutes(1);

        await RunAsync(_db, closed);

        var warning = _log.Records.Where(record => record.EventId.Id == 3101).ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain(nameof(FeedbackAvailability.NoRecipient));
        warning.Message.ShouldNotContain(nameof(FeedbackAvailability.Disabled));
        SendCount().ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WithTheDailyBudgetSpent_LeavesTheNoticeQueuedUntilTheWindowFrees()
    {
        for (var i = 0; i < FeedbackNotificationDispatchJob.DailyBudget; i++)
            await SubmitAsync(T0.AddSeconds(i));
        _clock.UtcNow = T0.AddMinutes(1);
        for (var sent = 0; sent < FeedbackNotificationDispatchJob.DailyBudget; sent += FeedbackNotificationDispatchJob.MaxSendsPerRun)
            await RunAsync(_db);
        SendCount().ShouldBe(FeedbackNotificationDispatchJob.DailyBudget);

        var over = await SubmitAsync(T0.AddMinutes(2));
        _clock.UtcNow = T0.AddMinutes(3);

        await RunAsync(_db);

        SendCount().ShouldBe(FeedbackNotificationDispatchJob.DailyBudget);
        (await NoticeAsync(_db, over.NoticeId)).State.ShouldBe(FeedbackNotificationState.Queued);
        _log.Records.Where(record => record.EventId.Id == 3107).ShouldHaveSingleItem()
            .Level.ShouldBe(LogLevel.Warning);

        _clock.UtcNow = T0.AddMinutes(1).AddHours(24).AddSeconds(1);

        await RunAsync(_db);

        SendCount().ShouldBe(FeedbackNotificationDispatchJob.DailyBudget + 1);
        (await NoticeAsync(_db, over.NoticeId)).State.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task RunAsync_ASendThatOutlastsTheRunBudget_StartsNoFurtherSend()
    {
        for (var i = 0; i < 3; i++)
            await SubmitAsync(T0.AddSeconds(i));
        _clock.UtcNow = T0.AddMinutes(1);
        // A slow provider: the clock passes the run budget while the first mail is handed over.
        _sender.SendFeedbackReceivedNotificationAsync(
                Arg.Any<string>(), Arg.Any<FeedbackReceivedNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _clock.UtcNow += FeedbackNotificationDispatchJob.RunBudget;
                return Task.CompletedTask;
            });

        await RunAsync(_db);

        SendCount().ShouldBe(1);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, eventId, formatter(state, exception)));
    }

    /// <summary>
    /// The host's shutdown token firing while a run saves its claim: the save is cancelled and nothing is written,
    /// so the notice is still Queued for the next run.
    /// </summary>
    private sealed class ShutdownDuringTheClaimSave : SaveChangesInterceptor
    {
        public int Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var claims = eventData.Context is { } context && context.ChangeTracker.Entries<FeedbackNotification>()
                .Any(entry => entry.State == EntityState.Modified
                    && entry.Entity.State == FeedbackNotificationState.Sending);
            if (!claims || Fired > 0)
                return ValueTask.FromResult(result);

            Fired++;
            throw new OperationCanceledException("The host is stopping.");
        }
    }
}
