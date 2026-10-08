using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback.Commands.SubmitFeedback;

/// <summary>
/// #1979 — one save of the submission, its operator notice and, the first time per page, the prompt suppression; a
/// key seen before replays its submission, even after the gate has closed. EF InMemory has no unique index, so the
/// two unique-violation branches (the same key saved concurrently, and a concurrent submission taking the page's
/// suppression) are proven against Postgres through the real endpoint in <c>FeedbackSubmitTests</c>.
/// </summary>
public sealed class SubmitFeedbackCommandHandlerTests : IAsyncDisposable
{
    private const string Recipient = "feedback-operator@example.test";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDbExceptionInspector _inspector = Substitute.For<IDbExceptionInspector>();
    private readonly FakeDateTimeProvider _clock = new(Now);
    private readonly Guid _userId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SubmitFeedbackCommandHandlerTests() => _currentUser.UserId.Returns(_userId);

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static FeedbackGate Gate(bool enabled = true, string? recipient = Recipient, bool canDeliver = true)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(canDeliver);
        return new FeedbackGate(
            Options.Create(new FeedbackOptions { Enabled = enabled, NotificationRecipient = recipient }), sender);
    }

    private SubmitFeedbackCommandHandler Handler(FeedbackGate? gate = null, ICurrentUser? user = null) =>
        new(_db, user ?? _currentUser, gate ?? Gate(), _clock, _inspector);

    private static SubmitFeedbackCommand Command(
        Guid key, string? page = "jobs", int? rating = null, string? comment = null, string? appVersion = null) =>
        new(key, page, rating, comment, ReportedClient.None, appVersion);

    private static FeedbackRating Stars(int value) => FeedbackRating.Create(value).Value;

    private async Task<JobSeekerId> RegisterAsync(Guid userId)
    {
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(_clock), _clock).Value;
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(Ct);
        return seeker.Id;
    }

    private async Task<(int Submissions, int Notices, int Suppressions)> CountAsync() =>
        (await _db.FeedbackSubmissions.CountAsync(Ct),
            await _db.FeedbackNotifications.CountAsync(Ct),
            await _db.FeedbackPromptSuppressions.CountAsync(Ct));

    [Fact]
    public async Task Handle_RatingOnly_SavesTheSubmissionItsNoticeDueAtOnceAndThePagesSuppression()
    {
        var owner = await RegisterAsync(_userId);
        var key = Guid.NewGuid();

        var result = await Handler().Handle(Command(key, rating: 4), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Replayed.ShouldBeFalse();

        var submission = await _db.FeedbackSubmissions.AsNoTracking().SingleAsync(Ct);
        submission.Id.Value.ShouldBe(result.Value.FeedbackId);
        submission.JobSeekerId.ShouldBe(owner);
        submission.SubmissionKey.ShouldBe(key);
        submission.Page.ShouldBe(FeedbackPage.Jobs);
        submission.Rating.ShouldBe(Stars(4));
        submission.Comment.ShouldBeNull();
        submission.Status.ShouldBe(FeedbackStatus.New);
        submission.SubmittedAt.ShouldBe(Now);

        var notice = await _db.FeedbackNotifications.AsNoTracking().SingleAsync(Ct);
        notice.SubmissionId.ShouldBe(submission.Id);
        notice.JobSeekerId.ShouldBe(owner);
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.Attempts.ShouldBe(0);
        notice.NextAttemptAt.ShouldBe(Now);

        var suppression = await _db.FeedbackPromptSuppressions.AsNoTracking().SingleAsync(Ct);
        suppression.JobSeekerId.ShouldBe(owner);
        suppression.Page.ShouldBe(FeedbackPage.Jobs);
    }

    [Fact]
    public async Task Handle_TextOnly_SavesTheTrimmedTextWithoutARating()
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), comment: "  Filtret glömmer min ort.  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Replayed.ShouldBeFalse();
        var submission = await _db.FeedbackSubmissions.AsNoTracking().SingleAsync(Ct);
        submission.Rating.ShouldBeNull();
        submission.Comment.ShouldNotBeNull().Value.ShouldBe("Filtret glömmer min ort.");
        (await CountAsync()).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Handle_ReportedClientAndAppVersion_AreStoredAsNarrowed()
    {
        await RegisterAsync(_userId);
        var client = new ReportedClient(
            1280, 720, 1920, 1080, 99m,
            ReportedTheme.Dark, ReportedDeviceClass.Desktop, ReportedOsFamily.Windows, ReportedBrowserFamily.Firefox);

        var result = await Handler().Handle(
            new SubmitFeedbackCommand(Guid.NewGuid(), "cv-review", 5, null, client, "c2c2c6cea"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var submission = await _db.FeedbackSubmissions.AsNoTracking().SingleAsync(Ct);
        submission.Page.ShouldBe(FeedbackPage.CvReview);
        submission.AppVersion.ShouldBe("c2c2c6cea");
        submission.Context.ViewportWidth.ShouldBe(1280);
        submission.Context.ViewportHeight.ShouldBe(720);
        submission.Context.ScreenWidth.ShouldBe(1920);
        submission.Context.ScreenHeight.ShouldBe(1080);
        submission.Context.PixelRatio.ShouldBeNull("a pixel ratio outside 0.25–10 is dropped, never refused");
        submission.Context.Theme.ShouldBe(ReportedTheme.Dark);
        submission.Context.DeviceClass.ShouldBe(ReportedDeviceClass.Desktop);
        submission.Context.OsFamily.ShouldBe(ReportedOsFamily.Windows);
        submission.Context.BrowserFamily.ShouldBe(ReportedBrowserFamily.Firefox);
    }

    [Fact]
    public async Task Handle_ASecondKeyForTheSamePage_AddsASubmissionButNoSecondSuppression()
    {
        await RegisterAsync(_userId);
        (await Handler().Handle(Command(Guid.NewGuid(), rating: 2), Ct)).IsSuccess.ShouldBeTrue();

        var second = await Handler().Handle(Command(Guid.NewGuid(), rating: 5), Ct);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Replayed.ShouldBeFalse();
        (await CountAsync()).ShouldBe((2, 2, 1));
    }

    [Fact]
    public async Task Handle_ASecondPage_GetsASuppressionOfItsOwn()
    {
        await RegisterAsync(_userId);
        (await Handler().Handle(Command(Guid.NewGuid(), "jobs", rating: 2), Ct)).IsSuccess.ShouldBeTrue();

        (await Handler().Handle(Command(Guid.NewGuid(), "cv", rating: 4), Ct)).IsSuccess.ShouldBeTrue();

        (await CountAsync()).ShouldBe((2, 2, 2));
        (await _db.FeedbackPromptSuppressions.AsNoTracking().Select(s => s.Page).ToListAsync(Ct))
            .ShouldBe([FeedbackPage.Jobs, FeedbackPage.Cv], ignoreOrder: true);
    }

    [Fact]
    public async Task Handle_AKnownKey_ReplaysTheSameSubmissionWithoutWritingAgain()
    {
        await RegisterAsync(_userId);
        var key = Guid.NewGuid();
        var first = await Handler().Handle(Command(key, rating: 2), Ct);

        var again = await Handler().Handle(Command(key, rating: 5, comment: "Jag ändrade mig."), Ct);

        again.IsSuccess.ShouldBeTrue();
        again.Value.ShouldBe(new FeedbackSubmitted(first.Value.FeedbackId, Replayed: true));
        (await CountAsync()).ShouldBe((1, 1, 1));
        var stored = await _db.FeedbackSubmissions.AsNoTracking().SingleAsync(Ct);
        stored.Rating.ShouldBe(Stars(2));
        stored.Comment.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, Recipient, true)]
    [InlineData(true, null, true)]
    [InlineData(true, Recipient, false)]
    public async Task Handle_AKnownKey_ReplaysEvenAfterTheGateHasClosed(bool enabled, string? recipient, bool canDeliver)
    {
        await RegisterAsync(_userId);
        var key = Guid.NewGuid();
        var first = await Handler().Handle(Command(key, rating: 3), Ct);

        var replay = await Handler(Gate(enabled, recipient, canDeliver)).Handle(Command(key, rating: 3), Ct);

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(new FeedbackSubmitted(first.Value.FeedbackId, Replayed: true));
        (await CountAsync()).ShouldBe((1, 1, 1));
    }

    [Theory]
    [InlineData(false, Recipient, true)]
    [InlineData(true, null, true)]
    [InlineData(true, Recipient, false)]
    public async Task Handle_ANewKeyWhileTheGateIsClosed_IsNotFoundAndWritesNothing(
        bool enabled, string? recipient, bool canDeliver)
    {
        await RegisterAsync(_userId);

        var result = await Handler(Gate(enabled, recipient, canDeliver)).Handle(Command(Guid.NewGuid(), rating: 3), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("Feedback.Closed");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("Jobs")]
    [InlineData("/jobb")]
    [InlineData("jobs ")]
    public async Task Handle_APageOutsideTheFixedSet_IsRefusedAndWritesNothing(string page)
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), page, rating: 3), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.UnknownPage");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Handle_NeitherARatingNorAText_IsRefusedAsEmpty(string? comment)
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), rating: null, comment: comment), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.Empty");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Handle_ARatingOutsideOneToFive_IsRefused(int rating)
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), rating: rating), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Code.ShouldBe("Feedback.RatingOutOfRange");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Handle_ATextOverTheLimit_IsRefused()
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(
            Command(Guid.NewGuid(), comment: new string('a', FeedbackComment.MaxLength + 1)), Ct);

        result.Error.Code.ShouldBe("Feedback.CommentTooLong");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Handle_AnAppVersionThatIsNotACommitHash_IsRefused()
    {
        await RegisterAsync(_userId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), rating: 3, appVersion: "dev"), Ct);

        result.Error.Code.ShouldBe("Feedback.AppVersionInvalid");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Handle_AUserWithoutAJobSeeker_IsNotFoundAndWritesNothing()
    {
        var result = await Handler().Handle(Command(Guid.NewGuid(), rating: 3), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
        (await CountAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Handle_AnotherUsersKey_IsANewSubmissionAndNotAReplay()
    {
        var mine = await RegisterAsync(_userId);
        var otherUserId = Guid.NewGuid();
        var theirs = await RegisterAsync(otherUserId);
        var other = Substitute.For<ICurrentUser>();
        other.UserId.Returns(otherUserId);
        var key = Guid.NewGuid();
        var first = await Handler().Handle(Command(key, rating: 2), Ct);

        var second = await Handler(user: other).Handle(Command(key, rating: 4), Ct);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Replayed.ShouldBeFalse();
        second.Value.FeedbackId.ShouldNotBe(first.Value.FeedbackId);
        (await _db.FeedbackSubmissions.AsNoTracking().Select(s => s.JobSeekerId).ToListAsync(Ct))
            .ShouldBe([mine, theirs], ignoreOrder: true);
    }

    [Fact]
    public void ToString_NeverPrintsTheText()
    {
        var command = new SubmitFeedbackCommand(
            Guid.NewGuid(), "jobs", 2, "Rekryteraren Anna Andersson svarade aldrig", ReportedClient.None, null);

        var printed = command.ToString();

        printed.ShouldNotContain("Anna");
        printed.ShouldNotContain("Rekryteraren");
        printed.ShouldContain("comment redacted");
    }

    [Fact]
    public void ToString_WithoutAText_SaysSo() =>
        Command(Guid.NewGuid(), rating: 4).ToString().ShouldContain("comment none");
}
