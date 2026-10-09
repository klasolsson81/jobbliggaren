using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #1979 — what the admin feedback routes answer, through the real host and Postgres: the list, one item, the
/// per-page summary (whose latest-rating count is SQL only Postgres can run), the availability, a status change with
/// its audit row, and a notice requeue.
/// <para>
/// The list and the summary read every submission in the shared database. Each test that asserts them owns page keys
/// no other test in the collection submits to (FeedbackSubmitTests keeps to jobs, overview, job-ad, matches,
/// applications, saved-ads and saved-searches), so its counts are exact.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class AdminFeedbackTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AdminAsync() =>
        (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;

    private static async Task<JsonElement> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await JsonAsync(response, Ct);
    }

    private static IReadOnlyList<JsonElement> ListItems(JsonElement list) =>
        [.. list.GetProperty("items").GetProperty("items").EnumerateArray()];

    private static IReadOnlyList<Guid> ListIds(JsonElement list) =>
        [.. ListItems(list).Select(item => item.GetProperty("id").GetGuid())];

    private static JsonElement SummaryOf(JsonElement summary, string pageKey) =>
        summary.GetProperty("pages").EnumerateArray().Single(page => page.GetProperty("pageKey").GetString() == pageKey);

    private static int[] RatedCounts(JsonElement page) =>
    [
        page.GetProperty("rated1").GetInt32(), page.GetProperty("rated2").GetInt32(), page.GetProperty("rated3").GetInt32(),
        page.GetProperty("rated4").GetInt32(), page.GetProperty("rated5").GetInt32(),
    ];

    private async Task<IReadOnlyList<(Guid? UserId, string AggregateType)>> AuditRowsAsync(Guid id, string eventType)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == id && row.EventType == eventType)
            .Select(row => new ValueTuple<Guid?, string>(row.UserId, row.AggregateType))
            .ToListAsync(Ct);
    }

    /// <summary>
    /// A rating sent <paramref name="ago"/> before now. The clock is the actor: the submit handler stamps SubmittedAt
    /// from IDateTimeProvider and saves the submission, its notice and the page's first suppression in one save, which
    /// this repeats with the handler's own factory calls. The summary reads feedback_submissions alone, so that the
    /// test account itself was opened today is beside what it measures.
    /// </summary>
    private async Task RateLongAgoAsync(Reporter reporter, FeedbackPage page, int stars, TimeSpan ago)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var at = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow - ago;
        var submission = FeedbackSubmission.Submit(
            reporter.JobSeekerId, Guid.NewGuid(), page, FeedbackRating.Create(stars).Value, null,
            ReportedClientContext.FromReported(null, null, null, null, null, null, null, null, null), null, at).Value;
        db.FeedbackSubmissions.Add(submission);
        db.FeedbackNotifications.Add(FeedbackNotification.QueueFor(submission));
        db.FeedbackPromptSuppressions.Add(FeedbackPromptSuppression.Record(reporter.JobSeekerId, page));
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task The_list_is_newest_first_with_excerpts_cut_on_the_server_and_counts_inside_the_page_filter()
    {
        var admin = await AdminAsync();
        var reporter = await ReporterAsync(factory, Ct);
        var longText = string.Concat(Enumerable.Repeat("Sökningen ", 12));
        var oldest = await SubmitNewAsync(reporter.Client, "statistics", 2, null, Ct);
        var middle = await SubmitNewAsync(reporter.Client, "statistics", null, longText, Ct);
        var newest = await SubmitNewAsync(reporter.Client, "statistics", 5, "Kort.", Ct);
        using (var changed = await admin.PostAsJsonAsync(StatusPath(oldest), new { status = "InProgress" }, Ct))
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await GetOkAsync(admin, $"{AdminPath}?page=statistics");

        ListIds(list).ShouldBe([newest, middle, oldest]);
        var items = ListItems(list);
        items[0].GetProperty("pageKey").GetString().ShouldBe("statistics");
        items[0].GetProperty("rating").GetInt32().ShouldBe(5);
        items[0].GetProperty("excerpt").GetString().ShouldBe("Kort.");
        items[0].GetProperty("status").GetString().ShouldBe("New");
        items[0].GetProperty("notificationState").GetString().ShouldBe("Queued");
        items[1].GetProperty("rating").ValueKind.ShouldBe(JsonValueKind.Null);
        items[1].GetProperty("excerpt").GetString().ShouldBe(longText[..90].TrimEnd() + "…");
        items[2].GetProperty("status").GetString().ShouldBe("InProgress");
        items[2].GetProperty("excerpt").ValueKind.ShouldBe(JsonValueKind.Null);
        list.GetProperty("items").GetProperty("totalCount").GetInt32().ShouldBe(3);
        var counts = list.GetProperty("counts");
        counts.GetProperty("all").GetInt32().ShouldBe(3);
        counts.GetProperty("new").GetInt32().ShouldBe(2);
        counts.GetProperty("inProgress").GetInt32().ShouldBe(1);
        counts.GetProperty("resolved").GetInt32().ShouldBe(0);
        counts.GetProperty("declined").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_pages_inside_the_page_filter()
    {
        var admin = await AdminAsync();
        var reporter = await ReporterAsync(factory, Ct);
        var first = await SubmitNewAsync(reporter.Client, "new-application", 1, null, Ct);
        var second = await SubmitNewAsync(reporter.Client, "new-application", 2, null, Ct);
        var third = await SubmitNewAsync(reporter.Client, "new-application", 3, null, Ct);
        using (var changed = await admin.PostAsJsonAsync(StatusPath(second), new { status = "Declined" }, Ct))
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var declined = await GetOkAsync(admin, $"{AdminPath}?page=new-application&status=Declined");
        var secondPage = await GetOkAsync(admin, $"{AdminPath}?page=new-application&pageNumber=2&pageSize=2");

        ListIds(declined).ShouldBe([second]);
        declined.GetProperty("items").GetProperty("totalCount").GetInt32().ShouldBe(1);
        declined.GetProperty("counts").GetProperty("all").GetInt32().ShouldBe(3);
        declined.GetProperty("counts").GetProperty("declined").GetInt32().ShouldBe(1);
        ListIds(secondPage).ShouldBe([first]);
        secondPage.GetProperty("items").GetProperty("totalCount").GetInt32().ShouldBe(3);
        secondPage.GetProperty("items").GetProperty("page").GetInt32().ShouldBe(2);
        ListIds(await GetOkAsync(admin, $"{AdminPath}?page=new-application&pageSize=2")).ShouldBe([third, second]);
    }

    [Fact]
    public async Task The_detail_carries_the_text_the_reported_client_the_notice_and_the_reporters_address()
    {
        var admin = await AdminAsync();
        var reporter = await ReporterAsync(factory, Ct);
        var id = await SubmitNewAsync(reporter.Client, "cv-review", 2, "  Granskningen missade min examen.  ", Ct);

        var detail = await GetOkAsync(admin, DetailPath(id));

        detail.GetProperty("id").GetGuid().ShouldBe(id);
        detail.GetProperty("pageKey").GetString().ShouldBe("cv-review");
        detail.GetProperty("rating").GetInt32().ShouldBe(2);
        detail.GetProperty("comment").GetString().ShouldBe("Granskningen missade min examen.");
        detail.GetProperty("status").GetString().ShouldBe("New");
        detail.GetProperty("statusChangedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("reporterEmail").GetString().ShouldBe(reporter.Email);
        detail.GetProperty("appVersion").GetString().ShouldBe(AppVersion);
        var client = detail.GetProperty("client");
        client.GetProperty("viewportWidth").GetInt32().ShouldBe(1280);
        client.GetProperty("screenHeight").GetInt32().ShouldBe(1080);
        client.GetProperty("pixelRatio").GetDecimal().ShouldBe(1.5m);
        client.GetProperty("theme").ValueKind.ShouldBe(JsonValueKind.Null);
        client.GetProperty("deviceClass").GetString().ShouldBe("Desktop");
        client.GetProperty("osFamily").GetString().ShouldBe("Windows");
        client.GetProperty("browserFamily").GetString().ShouldBe("Firefox");
        var notice = detail.GetProperty("notification");
        notice.GetProperty("state").GetString().ShouldBe("Queued");
        notice.GetProperty("attempts").GetInt32().ShouldBe(0);

        using var missing = await admin.GetAsync(DetailPath(Guid.NewGuid()), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_summary_counts_the_latest_rating_per_user_and_page_inside_the_window()
    {
        var admin = await AdminAsync();
        var changedMind = await ReporterAsync(factory, Ct);
        var ratedThenWrote = await ReporterAsync(factory, Ct);
        var onlyWrote = await ReporterAsync(factory, Ct);
        var ratedLongAgo = await ReporterAsync(factory, Ct);
        var ratedToday = await ReporterAsync(factory, Ct);

        await SubmitNewAsync(changedMind.Client, "activity-report", 2, null, Ct);
        await SubmitNewAsync(changedMind.Client, "activity-report", 5, null, Ct);
        await SubmitNewAsync(ratedThenWrote.Client, "activity-report", 4, null, Ct);
        await SubmitNewAsync(ratedThenWrote.Client, "activity-report", null, "En sak till.", Ct);
        await SubmitNewAsync(onlyWrote.Client, "activity-report", null, "Rapporten saknar maj.", Ct);
        await SubmitNewAsync(onlyWrote.Client, "application-history", null, "Historiken är tom.", Ct);
        await RateLongAgoAsync(ratedLongAgo, FeedbackPage.CvImport, 1, TimeSpan.FromDays(40));
        await SubmitNewAsync(ratedToday.Client, "cv-import", 5, null, Ct);

        var month = await GetOkAsync(admin, SummaryPath(30));
        var quarter = await GetOkAsync(admin, SummaryPath(90));

        month.GetProperty("days").GetInt32().ShouldBe(30);
        var report = SummaryOf(month, "activity-report");
        report.GetProperty("submissions").GetInt32().ShouldBe(5);
        report.GetProperty("raters").GetInt32().ShouldBe(2);
        RatedCounts(report).ShouldBe([0, 0, 0, 1, 1]);
        report.GetProperty("mean").GetDecimal().ShouldBe(4.5m);

        var unrated = SummaryOf(month, "application-history");
        unrated.GetProperty("submissions").GetInt32().ShouldBe(1);
        unrated.GetProperty("raters").GetInt32().ShouldBe(0);
        RatedCounts(unrated).ShouldBe([0, 0, 0, 0, 0]);
        unrated.GetProperty("mean").ValueKind.ShouldBe(JsonValueKind.Null);

        var importThisMonth = SummaryOf(month, "cv-import");
        importThisMonth.GetProperty("submissions").GetInt32().ShouldBe(1);
        importThisMonth.GetProperty("raters").GetInt32().ShouldBe(1);
        RatedCounts(importThisMonth).ShouldBe([0, 0, 0, 0, 1]);
        importThisMonth.GetProperty("mean").GetDecimal().ShouldBe(5m);

        var importThisQuarter = SummaryOf(quarter, "cv-import");
        importThisQuarter.GetProperty("submissions").GetInt32().ShouldBe(2);
        importThisQuarter.GetProperty("raters").GetInt32().ShouldBe(2);
        RatedCounts(importThisQuarter).ShouldBe([1, 0, 0, 0, 1]);
        importThisQuarter.GetProperty("mean").GetDecimal().ShouldBe(3m);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(0)]
    public async Task A_summary_window_other_than_7_30_or_90_days_is_400(int days)
    {
        var admin = await AdminAsync();

        using var response = await admin.GetAsync(SummaryPath(days), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_status_change_answers_204_and_writes_one_audit_row_for_the_administrator()
    {
        var (admin, adminId, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var reporter = await ReporterAsync(factory, Ct);
        var id = await SubmitNewAsync(reporter.Client, "my-pages", 4, null, Ct);

        using var changed = await admin.PostAsJsonAsync(StatusPath(id), new { status = "Resolved" }, Ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detail = await GetOkAsync(admin, DetailPath(id));
        detail.GetProperty("status").GetString().ShouldBe("Resolved");
        detail.GetProperty("statusChangedAt").ValueKind.ShouldBe(JsonValueKind.String);
        (await AuditRowsAsync(id, "Admin.FeedbackStatusChanged")).ShouldHaveSingleItem()
            .ShouldBe(((Guid?)adminId, "Feedback"));

        using var unchanged = await admin.PostAsJsonAsync(StatusPath(id), new { status = "Resolved" }, Ct);
        unchanged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(unchanged, Ct)).ShouldBe("Feedback.StatusUnchanged");
        using var undefined = await admin.PostAsJsonAsync(StatusPath(id), new { status = 42 }, Ct);
        undefined.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var missing = await admin.PostAsJsonAsync(StatusPath(Guid.NewGuid()), new { status = "Resolved" }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemTitleAsync(missing, Ct)).ShouldBe("Feedback.NotFound");

        (await AuditRowsAsync(id, "Admin.FeedbackStatusChanged")).Count.ShouldBe(1, "a refused change is not audited");
    }

    [Fact]
    public async Task Requeueing_an_unknown_notice_needs_the_duplicate_risk_acknowledged()
    {
        var (admin, adminId, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var reporter = await ReporterAsync(factory, Ct);
        var id = await SubmitNewAsync(reporter.Client, "followed-companies", 2, null, Ct);

        using (var queued = await admin.PostAsJsonAsync(RequeuePath(id), new { acknowledgeDuplicateRisk = true }, Ct))
        {
            queued.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemTitleAsync(queued, Ct)).ShouldBe("Feedback.NotificationNotRequeueable");
        }

        await LoseTheOutcomeAsync(factory, id, Ct);

        using (var unacknowledged = await admin.PostAsJsonAsync(
                   RequeuePath(id), new { acknowledgeDuplicateRisk = false }, Ct))
        {
            unacknowledged.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemTitleAsync(unacknowledged, Ct)).ShouldBe("Feedback.DuplicateRiskNotAcknowledged");
        }

        using (var omitted = await admin.PostAsJsonAsync(RequeuePath(id), new { }, Ct))
            omitted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetOkAsync(admin, DetailPath(id))).GetProperty("notification").GetProperty("state").GetString()
            .ShouldBe("Unknown");

        using (var acknowledged = await admin.PostAsJsonAsync(
                   RequeuePath(id), new { acknowledgeDuplicateRisk = true }, Ct))
            acknowledged.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var notice = (await GetOkAsync(admin, DetailPath(id))).GetProperty("notification");
        notice.GetProperty("state").GetString().ShouldBe("Queued");
        notice.GetProperty("attempts").GetInt32().ShouldBe(0);
        (await AuditRowsAsync(id, "Admin.FeedbackNotificationRequeued")).ShouldHaveSingleItem()
            .ShouldBe(((Guid?)adminId, "Feedback"));

        using var missing = await admin.PostAsJsonAsync(
            RequeuePath(Guid.NewGuid()), new { acknowledgeDuplicateRisk = true }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_availability_names_why_feedback_is_open_or_closed()
    {
        var admin = await AdminAsync();

        (await GetOkAsync(admin, $"{AdminPath}/availability")).GetProperty("availability").GetString()
            .ShouldBe("Open");

        using (factory.Emails.Incapable())
        {
            (await GetOkAsync(admin, $"{AdminPath}/availability")).GetProperty("availability").GetString()
                .ShouldBe("CannotDeliver");
        }
    }
}
