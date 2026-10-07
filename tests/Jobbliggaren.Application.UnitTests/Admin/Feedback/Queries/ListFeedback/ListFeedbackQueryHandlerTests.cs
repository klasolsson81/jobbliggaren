using Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Feedback;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Queries.ListFeedback;

/// <summary>
/// #1979 — the admin's list: newest first, an excerpt cut on the server, the notice's state, and counts per status
/// inside the page filter so a filter label never shows a count it did not take. Submission times are distinct on
/// purpose: EF InMemory cannot order the typed id the handler breaks ties on, and real Postgres can.
/// </summary>
public sealed class ListFeedbackQueryHandlerTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly JobSeekerId _owner = new(Guid.NewGuid());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private Task<FeedbackRows.Saved> SubmitAsync(
        FeedbackPage page, DateTimeOffset at, int? rating = 3, string? comment = null) =>
        FeedbackRows.SubmitAsync(_db, _owner, page, rating, comment, at, Ct);

    /// <summary>An administrator's status change, as <c>ChangeFeedbackStatusCommandHandler</c> applies it.</summary>
    private async Task ChangeStatusAsync(FeedbackRows.Saved saved, FeedbackStatus to)
    {
        var submission = await _db.FeedbackSubmissions.SingleAsync(s => s.Id == saved.SubmissionId, Ct);
        submission.ChangeStatus(to, T0.AddDays(1)).IsSuccess.ShouldBeTrue();
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();
    }

    private Task<FeedbackListDto> ListAsync(ListFeedbackQuery query) =>
        new ListFeedbackQueryHandler(_db).Handle(query, Ct).AsTask();

    [Fact]
    public async Task Handle_ListsNewestFirstWithThePageKeyRatingStatusTimeAndNoticeState()
    {
        var oldest = await SubmitAsync(FeedbackPage.Jobs, T0, rating: 2);
        var middle = await SubmitAsync(FeedbackPage.Cv, T0.AddMinutes(1), rating: null, comment: "Kort text.");
        var newest = await SubmitAsync(FeedbackPage.Statistics, T0.AddMinutes(2), rating: 5);

        var list = await ListAsync(new ListFeedbackQuery());

        list.Items.Items.Select(item => item.Id)
            .ShouldBe([newest.SubmissionId.Value, middle.SubmissionId.Value, oldest.SubmissionId.Value]);
        var first = list.Items.Items[0];
        first.PageKey.ShouldBe("statistics");
        first.Rating.ShouldBe(5);
        first.Excerpt.ShouldBeNull();
        first.Status.ShouldBe(FeedbackStatus.New);
        first.SubmittedAt.ShouldBe(T0.AddMinutes(2));
        first.NotificationState.ShouldBe(FeedbackNotificationState.Queued);
        var textOnly = list.Items.Items[1];
        textOnly.Rating.ShouldBeNull();
        textOnly.Excerpt.ShouldBe("Kort text.");
        list.Items.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_ATextLongerThanTheExcerpt_IsCutAtNinetyCharactersWithAnEllipsis()
    {
        var longText = string.Concat(Enumerable.Repeat("abcdefghij", 12));
        var exact = new string('x', ListFeedbackQuery.ExcerptLength);
        var spaceAtTheCut = new string('y', ListFeedbackQuery.ExcerptLength - 1) + " och mer text efter snittet";
        await SubmitAsync(FeedbackPage.Jobs, T0, rating: null, comment: longText);
        await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(1), rating: null, comment: exact);
        await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(2), rating: null, comment: spaceAtTheCut);

        var excerpts = (await ListAsync(new ListFeedbackQuery())).Items.Items.Select(item => item.Excerpt).ToList();

        excerpts[2].ShouldBe(longText[..ListFeedbackQuery.ExcerptLength] + "…");
        excerpts[1].ShouldBe(exact);
        excerpts[0].ShouldBe(new string('y', ListFeedbackQuery.ExcerptLength - 1) + "…");
    }

    [Fact]
    public async Task Handle_CountsPerStatusInsideThePageFilter()
    {
        var jobsNew = await SubmitAsync(FeedbackPage.Jobs, T0);
        await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(1));
        var jobsInProgress = await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(2));
        var jobsResolved = await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(3));
        var cvDeclined = await SubmitAsync(FeedbackPage.Cv, T0.AddMinutes(4));
        await SubmitAsync(FeedbackPage.Cv, T0.AddMinutes(5));
        await ChangeStatusAsync(jobsInProgress, FeedbackStatus.InProgress);
        await ChangeStatusAsync(jobsResolved, FeedbackStatus.Resolved);
        await ChangeStatusAsync(cvDeclined, FeedbackStatus.Declined);

        var jobs = await ListAsync(new ListFeedbackQuery(PageKey: "jobs"));
        var everything = await ListAsync(new ListFeedbackQuery());

        jobs.Counts.ShouldBe(new FeedbackStatusCountsDto(All: 4, New: 2, InProgress: 1, Resolved: 1, Declined: 0));
        jobs.Items.TotalCount.ShouldBe(4);
        jobs.Items.Items.ShouldAllBe(item => item.PageKey == "jobs");
        jobs.Items.Items.Select(item => item.Id).ShouldContain(jobsNew.SubmissionId.Value);
        everything.Counts.ShouldBe(new FeedbackStatusCountsDto(All: 6, New: 3, InProgress: 1, Resolved: 1, Declined: 1));
    }

    [Fact]
    public async Task Handle_AStatusFilter_ListsThatStatusWithItsTotalAndKeepsEveryCount()
    {
        await SubmitAsync(FeedbackPage.Jobs, T0);
        var inProgress = await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(1));
        await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(2));
        await ChangeStatusAsync(inProgress, FeedbackStatus.InProgress);

        var list = await ListAsync(new ListFeedbackQuery(Status: FeedbackStatus.InProgress));

        list.Items.Items.Select(item => item.Id).ShouldBe([inProgress.SubmissionId.Value]);
        list.Items.Items[0].Status.ShouldBe(FeedbackStatus.InProgress);
        list.Items.TotalCount.ShouldBe(1);
        list.Counts.ShouldBe(new FeedbackStatusCountsDto(All: 3, New: 2, InProgress: 1, Resolved: 0, Declined: 0));
    }

    [Fact]
    public async Task Handle_APageNumberAndSize_ReturnThatSliceOfTheNewestFirstOrder()
    {
        var saved = new List<FeedbackRows.Saved>();
        for (var i = 0; i < 5; i++)
            saved.Add(await SubmitAsync(FeedbackPage.Jobs, T0.AddMinutes(i)));

        var second = await ListAsync(new ListFeedbackQuery(PageNumber: 2, PageSize: 2));
        var last = await ListAsync(new ListFeedbackQuery(PageNumber: 3, PageSize: 2));

        second.Items.Items.Select(item => item.Id).ShouldBe([saved[2].SubmissionId.Value, saved[1].SubmissionId.Value]);
        second.Items.TotalCount.ShouldBe(5);
        second.Items.Page.ShouldBe(2);
        second.Items.PageSize.ShouldBe(2);
        last.Items.Items.Select(item => item.Id).ShouldBe([saved[0].SubmissionId.Value]);
    }

    [Fact]
    public async Task Handle_ANoticeTheDispatchJobHasMoved_ShowsItsCurrentState()
    {
        var saved = await SubmitAsync(FeedbackPage.Jobs, T0);
        // FeedbackNotificationDispatchJob's own transforms for an accepted send: the claim, then the outcome.
        var notice = await _db.FeedbackNotifications.SingleAsync(n => n.Id == saved.NoticeId, Ct);
        notice.Claim(T0.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        notice.RecordAccepted(T0.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await _db.SaveChangesAsync(Ct);
        _db.ClearTracking();

        var list = await ListAsync(new ListFeedbackQuery());

        list.Items.Items.ShouldHaveSingleItem().NotificationState.ShouldBe(FeedbackNotificationState.Accepted);
    }

    [Fact]
    public async Task Handle_NoFeedback_IsAnEmptyPageWithZeroCounts()
    {
        var list = await ListAsync(new ListFeedbackQuery());

        list.Items.Items.ShouldBeEmpty();
        list.Items.TotalCount.ShouldBe(0);
        list.Counts.ShouldBe(new FeedbackStatusCountsDto(0, 0, 0, 0, 0));
    }
}
