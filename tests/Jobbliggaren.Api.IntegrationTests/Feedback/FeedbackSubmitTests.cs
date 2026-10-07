using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #1979 — <c>POST /api/v1/me/feedback</c> and <c>GET /api/v1/me/feedback/prompt-state</c> through the real host and
/// Postgres. The unique indexes are what make the submit idempotent and the suppression single, so the races are run
/// here: in parallel, and staged by <see cref="FeedbackSaveRace"/> so the unique-violation branches run every time
/// rather than when the scheduler happens to interleave them.
/// </summary>
[Collection("Api")]
public sealed class FeedbackSubmitTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonPromptState> PromptStateAsync(HttpClient client)
    {
        using var response = await client.GetAsync(PromptStatePath, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        var body = await JsonAsync(response, Ct);
        return new JsonPromptState(
            body.GetProperty("open").GetBoolean(),
            [.. body.GetProperty("answeredPages").EnumerateArray().Select(page => page.GetString()!)]);
    }

    private sealed record JsonPromptState(bool Open, IReadOnlyList<string> AnsweredPages);

    [Fact]
    public async Task A_rating_alone_is_saved_with_its_queued_notice_and_the_page_suppressed()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();

        using var response = await SubmitAsync(reporter.Client, Payload(key, "jobs", rating: 4), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (id, replayed) = await SubmittedAsync(response, Ct);
        replayed.ShouldBeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var submission = await db.FeedbackSubmissions.AsNoTracking()
            .SingleAsync(s => s.JobSeekerId == reporter.JobSeekerId, Ct);
        submission.Id.Value.ShouldBe(id);
        submission.SubmissionKey.ShouldBe(key);
        submission.Page.ShouldBe(FeedbackPage.Jobs);
        submission.Rating.ShouldBe(FeedbackRating.Create(4).Value);
        submission.Comment.ShouldBeNull();
        submission.AppVersion.ShouldBe(AppVersion);
        submission.Context.ViewportWidth.ShouldBe(1280);
        submission.Context.PixelRatio.ShouldBe(1.5m);
        submission.Context.Theme.ShouldBe(ReportedTheme.Dark);
        submission.Context.DeviceClass.ShouldBe(ReportedDeviceClass.Desktop);
        submission.Context.OsFamily.ShouldBe(ReportedOsFamily.Windows);
        submission.Context.BrowserFamily.ShouldBe(ReportedBrowserFamily.Firefox);

        var notice = await db.FeedbackNotifications.AsNoTracking()
            .SingleAsync(n => n.JobSeekerId == reporter.JobSeekerId, Ct);
        notice.SubmissionId.Value.ShouldBe(id);
        notice.State.ShouldBe(FeedbackNotificationState.Queued);
        notice.Attempts.ShouldBe(0);

        (await db.FeedbackPromptSuppressions.AsNoTracking()
                .Where(s => s.JobSeekerId == reporter.JobSeekerId)
                .Select(s => s.Page)
                .ToListAsync(Ct))
            .ShouldBe([FeedbackPage.Jobs]);
    }

    [Fact]
    public async Task A_text_alone_is_saved_trimmed_without_a_rating()
    {
        var reporter = await ReporterAsync(factory, Ct);

        using var response = await SubmitAsync(
            reporter.Client, Payload(Guid.NewGuid(), "overview", comment: "  Filtret glömmer min ort.  "), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var submission = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().FeedbackSubmissions
            .AsNoTracking()
            .SingleAsync(s => s.JobSeekerId == reporter.JobSeekerId, Ct);
        submission.Rating.ShouldBeNull();
        submission.Comment.ShouldNotBeNull().Value.ShouldBe("Filtret glömmer min ort.");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Fact]
    public async Task Neither_a_rating_nor_a_text_is_400_and_stores_nothing()
    {
        var reporter = await ReporterAsync(factory, Ct);

        using var response = await SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "jobs"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.Empty");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("Jobs")]
    [InlineData("/jobb")]
    public async Task A_page_outside_the_fixed_set_is_400_and_stores_nothing(string page)
    {
        var reporter = await ReporterAsync(factory, Ct);

        using var response = await SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), page, rating: 3), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.UnknownPage");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_is_400()
    {
        var reporter = await ReporterAsync(factory, Ct);

        using var response = await reporter.Client.PostAsJsonAsync(
            SubmitPath, Payload(Guid.NewGuid(), "jobs", rating: 3), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.InvalidSubmission");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
    }

    [Fact]
    public async Task A_file_part_is_refused_with_400_never_ignored()
    {
        var reporter = await ReporterAsync(factory, Ct);
        using var form = Form(Payload(Guid.NewGuid(), "jobs", rating: 3));
        var screenshot = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        screenshot.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(screenshot, "screenshot", "skarmdump.png");

        using var response = await reporter.Client.PostAsync(SubmitPath, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.InvalidSubmission");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
    }

    [Theory]
    [InlineData("{inte json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task A_payload_that_is_not_a_submission_is_400(string payload)
    {
        var reporter = await ReporterAsync(factory, Ct);
        using var form = new MultipartFormDataContent { { new StringContent(payload), "payload" } };

        using var response = await reporter.Client.PostAsync(SubmitPath, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.InvalidSubmission");
    }

    [Fact]
    public async Task A_body_over_the_size_bound_is_400_and_stores_nothing()
    {
        var reporter = await ReporterAsync(factory, Ct);

        using var response = await SubmitAsync(
            reporter.Client, Payload(Guid.NewGuid(), "jobs", comment: new string('a', 70_000)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Refused while the form is read, before the domain's own 2 000-character bound could answer.
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.InvalidSubmission");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
    }

    [Fact]
    public async Task The_same_key_twice_answers_201_then_200_with_the_same_submission()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();

        using var first = await SubmitAsync(reporter.Client, Payload(key, "matches", rating: 3), Ct);
        using var second = await SubmitAsync(reporter.Client, Payload(key, "matches", rating: 3), Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (firstId, firstReplayed) = await SubmittedAsync(first, Ct);
        var (secondId, secondReplayed) = await SubmittedAsync(second, Ct);
        firstReplayed.ShouldBeFalse();
        secondReplayed.ShouldBeTrue();
        secondId.ShouldBe(firstId);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Fact]
    public async Task Five_parallel_posts_with_one_key_all_succeed_with_one_submission()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => SubmitAsync(reporter.Client, Payload(key, "matches", rating: 4), Ct)));
        try
        {
            responses.Select(r => r.StatusCode)
                .ShouldAllBe(status => status == HttpStatusCode.Created || status == HttpStatusCode.OK);
            responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            var answers = new List<(Guid Id, bool Replayed)>();
            foreach (var response in responses)
                answers.Add(await SubmittedAsync(response, Ct));
            answers.Select(answer => answer.Id).Distinct().ShouldHaveSingleItem();
            answers.Count(answer => !answer.Replayed).ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Fact]
    public async Task Two_keys_for_one_page_in_parallel_save_two_submissions_and_one_suppression()
    {
        var reporter = await ReporterAsync(factory, Ct);

        var responses = await Task.WhenAll(
            SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "applications", rating: 2), Ct),
            SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "applications", rating: 5), Ct));
        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
            var first = await SubmittedAsync(responses[0], Ct);
            var second = await SubmittedAsync(responses[1], Ct);
            first.Id.ShouldNotBe(second.Id);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(2, 2, 1));
    }

    [Fact]
    public async Task A_save_that_loses_the_pages_suppression_to_a_concurrent_submission_is_saved_again_without_it()
    {
        var reporter = await ReporterAsync(factory, Ct);
        HttpResponseMessage? competitor = null;
        factory.FeedbackSaveRace.Arm(reporter.JobSeekerId, async raceCt =>
            competitor = await SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "saved-ads", rating: 5), raceCt));
        HttpResponseMessage held;
        try
        {
            held = await SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "saved-ads", rating: 1), Ct);
        }
        finally
        {
            factory.FeedbackSaveRace.Disarm();
        }

        using (held)
        using (competitor)
        {
            factory.FeedbackSaveRace.Fired.ShouldBe(1);
            competitor.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Created);
            held.StatusCode.ShouldBe(HttpStatusCode.Created, await held.Content.ReadAsStringAsync(Ct));
            var heldAnswer = await SubmittedAsync(held, Ct);
            var competitorAnswer = await SubmittedAsync(competitor, Ct);
            heldAnswer.Replayed.ShouldBeFalse();
            heldAnswer.Id.ShouldNotBe(competitorAnswer.Id);
        }

        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(2, 2, 1));
    }

    [Fact]
    public async Task A_save_that_loses_its_key_to_a_concurrent_retry_replays_the_winner()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();
        HttpResponseMessage? competitor = null;
        factory.FeedbackSaveRace.Arm(reporter.JobSeekerId, async raceCt =>
            competitor = await SubmitAsync(reporter.Client, Payload(key, "saved-searches", rating: 4), raceCt));
        HttpResponseMessage held;
        try
        {
            held = await SubmitAsync(reporter.Client, Payload(key, "saved-searches", rating: 4), Ct);
        }
        finally
        {
            factory.FeedbackSaveRace.Disarm();
        }

        using (held)
        using (competitor)
        {
            factory.FeedbackSaveRace.Fired.ShouldBe(1);
            competitor.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Created);
            held.StatusCode.ShouldBe(HttpStatusCode.OK, await held.Content.ReadAsStringAsync(Ct));
            var winner = await SubmittedAsync(competitor, Ct);
            (await SubmittedAsync(held, Ct)).ShouldBe((winner.Id, true));
        }

        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Fact]
    public async Task Rating_the_same_page_again_keeps_both_submissions()
    {
        var reporter = await ReporterAsync(factory, Ct);

        var first = await SubmitNewAsync(reporter.Client, "applications", 2, null, Ct);
        var again = await SubmitNewAsync(reporter.Client, "applications", 4, "Bättre nu.", Ct);

        again.ShouldNotBe(first);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(2, 2, 1));
    }

    [Fact]
    public async Task The_prompt_state_lists_a_page_once_feedback_was_given_there()
    {
        var reporter = await ReporterAsync(factory, Ct);

        var before = await PromptStateAsync(reporter.Client);
        await SubmitNewAsync(reporter.Client, "job-ad", 5, null, Ct);
        await SubmitNewAsync(reporter.Client, "applications", null, "Listan laddar långsamt.", Ct);
        var after = await PromptStateAsync(reporter.Client);

        before.Open.ShouldBeTrue();
        before.AnsweredPages.ShouldBeEmpty();
        after.Open.ShouldBeTrue();
        after.AnsweredPages.ShouldBe(["applications", "job-ad"]);
    }

    [Fact]
    public async Task Both_routes_answer_401_without_a_session()
    {
        var anonymous = factory.CreateClient();

        using var submit = await SubmitAsync(anonymous, Payload(Guid.NewGuid(), "jobs", rating: 3), Ct);
        using var promptState = await anonymous.GetAsync(PromptStatePath, Ct);

        submit.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        promptState.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_the_gate_closed_a_new_submission_is_404_and_the_prompt_state_is_closed()
    {
        var reporter = await ReporterAsync(factory, Ct);
        await SubmitNewAsync(reporter.Client, "jobs", 3, null, Ct);

        JsonPromptState closedState;
        using (factory.Emails.Incapable())
        {
            using var refused = await SubmitAsync(reporter.Client, Payload(Guid.NewGuid(), "overview", rating: 3), Ct);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemTitleAsync(refused, Ct)).ShouldBe("Feedback.Closed");
            closedState = await PromptStateAsync(reporter.Client);
        }

        closedState.Open.ShouldBeFalse();
        closedState.AnsweredPages.ShouldBeEmpty();
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
        (await PromptStateAsync(reporter.Client)).AnsweredPages.ShouldBe(["jobs"]);
    }

    [Fact]
    public async Task A_known_key_replays_even_after_the_gate_has_closed()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();
        using var first = await SubmitAsync(reporter.Client, Payload(key, "jobs", rating: 2), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (id, _) = await SubmittedAsync(first, Ct);

        using (factory.Emails.Incapable())
        {
            using var replay = await SubmitAsync(reporter.Client, Payload(key, "jobs", rating: 2), Ct);

            replay.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await SubmittedAsync(replay, Ct)).ShouldBe((id, true));
        }

        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }
}
