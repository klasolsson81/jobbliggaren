using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.GetFeedbackDetail;

/// <summary>
/// #1979 — one submission for the admin: the full text, what the browser reported, the notice's delivery state, and
/// the reporter's address read on the server by the account the submission belongs to.
/// </summary>
public sealed class GetFeedbackDetailQueryHandlerTests : IAsyncDisposable
{
    private const string ReporterEmail = "reporter@example.se";
    private const string Text = "Rekryteraren Anna Andersson svarade aldrig.";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly FakeDateTimeProvider _clock = new(T0.AddDays(-30));
    private readonly Guid _userId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetFeedbackDetailQueryHandlerTests() =>
        _accounts.GetEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns(ReporterEmail);

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<JobSeeker> RegisterAsync()
    {
        var seeker = JobSeeker.Register(_userId, TermsAcceptance.AcceptCurrent(_clock), _clock).Value;
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(Ct);
        return seeker;
    }

    private Task<FeedbackDetailDto?> DetailAsync(Guid id) =>
        new GetFeedbackDetailQueryHandler(_db, _accounts).Handle(new GetFeedbackDetailQuery(id), Ct).AsTask();

    [Fact]
    public async Task Handle_ReturnsTheFullSubmissionItsClientItsNoticeAndTheReportersAddress()
    {
        var seeker = await RegisterAsync();
        var client = ReportedClientContext.FromReported(
            390, 844, 390, 844, 3m,
            ReportedTheme.Light, ReportedDeviceClass.Mobile, ReportedOsFamily.Ios, ReportedBrowserFamily.Safari);
        var saved = await FeedbackRows.SubmitAsync(
            _db, seeker.Id, FeedbackPage.JobAd, 2, Text, T0, client, "c2c2c6cea", Ct);

        var detail = (await DetailAsync(saved.SubmissionId.Value)).ShouldNotBeNull();

        detail.Id.ShouldBe(saved.SubmissionId.Value);
        detail.PageKey.ShouldBe("job-ad");
        detail.Rating.ShouldBe(2);
        detail.Comment.ShouldBe(Text);
        detail.Status.ShouldBe(FeedbackStatus.New);
        detail.SubmittedAt.ShouldBe(T0);
        detail.StatusChangedAt.ShouldBeNull();
        detail.ReporterEmail.ShouldBe(ReporterEmail);
        detail.AppVersion.ShouldBe("c2c2c6cea");
        detail.Client.ShouldBe(new ReportedClientDto(
            390, 844, 390, 844, 3m,
            ReportedTheme.Light, ReportedDeviceClass.Mobile, ReportedOsFamily.Ios, ReportedBrowserFamily.Safari));
        var notice = detail.Notification.ShouldNotBeNull();
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.Attempts.ShouldBe(0);
        notice.NextAttemptAt.ShouldBe(T0);
        notice.AcceptedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_AnOwnerInsideTheRestoreWindow_IsStillResolvedToTheAddress()
    {
        var seeker = await RegisterAsync();
        var saved = await FeedbackRows.SubmitAsync(_db, seeker.Id, FeedbackPage.Jobs, 4, null, T0, Ct);
        // The self-service account deletion's transform: soft-deleted, inside its 30-day restore window.
        var tracked = await _db.JobSeekers.SingleAsync(js => js.Id == seeker.Id, Ct);
        tracked.SoftDelete(new FakeDateTimeProvider(T0.AddDays(1)));
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
        (await _db.JobSeekers.AnyAsync(js => js.Id == seeker.Id, Ct))
            .ShouldBeFalse("precondition: the default query filter hides the soft-deleted owner");

        var detail = await DetailAsync(saved.SubmissionId.Value);

        detail.ShouldNotBeNull().ReporterEmail.ShouldBe(ReporterEmail);
    }

    [Fact]
    public async Task Handle_ANoticeTheDispatchJobAccepted_ShowsItsAttemptAndAcceptance()
    {
        var seeker = await RegisterAsync();
        var saved = await FeedbackRows.SubmitAsync(_db, seeker.Id, FeedbackPage.Jobs, 4, null, T0, Ct);
        // FeedbackNotificationDispatchJob's own transforms for an accepted send: the claim, then the outcome.
        var notice = await _db.FeedbackNotifications.SingleAsync(n => n.Id == saved.NoticeId, Ct);
        notice.Claim(T0.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        notice.RecordAccepted(T0.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var shown = (await DetailAsync(saved.SubmissionId.Value)).ShouldNotBeNull().Notification.ShouldNotBeNull();

        shown.State.ShouldBe(FeedbackNotificationState.Accepted);
        shown.Attempts.ShouldBe(1);
        shown.AcceptedAt.ShouldBe(T0.AddMinutes(1));
        shown.StateChangedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public async Task Handle_AnUnknownId_IsNull()
    {
        await RegisterAsync();

        (await DetailAsync(Guid.NewGuid())).ShouldBeNull();
        await _accounts.DidNotReceiveWithAnyArgs().GetEmailAsync(default, Ct);
    }

    [Fact]
    public async Task ToString_NeverPrintsTheTextOrTheAddress()
    {
        var seeker = await RegisterAsync();
        var saved = await FeedbackRows.SubmitAsync(_db, seeker.Id, FeedbackPage.Jobs, null, Text, T0, Ct);

        var printed = (await DetailAsync(saved.SubmissionId.Value)).ShouldNotBeNull().ToString();

        printed.ShouldNotContain("Anna");
        printed.ShouldNotContain(ReporterEmail);
        printed.ShouldContain(saved.SubmissionId.Value.ToString());
    }
}
