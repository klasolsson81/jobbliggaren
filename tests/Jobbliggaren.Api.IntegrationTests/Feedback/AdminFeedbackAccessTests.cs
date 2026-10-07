using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #1979 — who may use the admin feedback routes: the Admin policy on every one of the six, answered before any
/// handler runs, and every answer an administrator gets is private and never stored.
/// </summary>
[Collection("Api")]
public sealed class AdminFeedbackAccessTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Answer(string Route, HttpStatusCode Status, CacheControlHeaderValue? CacheControl);

    /// <summary>The four reads, then a status change and an acknowledged requeue of <paramref name="id"/>.</summary>
    private static async Task<IReadOnlyList<Answer>> EveryRouteAsync(HttpClient client, Guid id)
    {
        string[] reads = [AdminPath, DetailPath(id), SummaryPath(30), $"{AdminPath}/availability"];
        var answers = new List<Answer>();
        foreach (var path in reads)
        {
            using var read = await client.GetAsync(path, Ct);
            answers.Add(new Answer($"GET {path}", read.StatusCode, read.Headers.CacheControl));
        }

        using (var status = await client.PostAsJsonAsync(StatusPath(id), new { status = "InProgress" }, Ct))
            answers.Add(new Answer("POST status", status.StatusCode, status.Headers.CacheControl));
        using (var requeue = await client.PostAsJsonAsync(RequeuePath(id), new { acknowledgeDuplicateRisk = true }, Ct))
            answers.Add(new Answer("POST requeue", requeue.StatusCode, requeue.Headers.CacheControl));
        return answers;
    }

    /// <summary>A submission whose notice ended Unknown, so every route — the requeue included — has work to do.</summary>
    private async Task<(Guid Id, Reporter Reporter)> RequeueableAsync()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var id = await SubmitNewAsync(reporter.Client, "cv", 3, null, Ct);
        await LoseTheOutcomeAsync(factory, id, Ct);
        return (id, reporter);
    }

    private async Task<(FeedbackStatus Status, FeedbackNotificationState Notice)> StoredAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var submissionId = new FeedbackSubmissionId(id);
        return (
            await db.FeedbackSubmissions.Where(s => s.Id == submissionId).Select(s => s.Status).SingleAsync(Ct),
            await db.FeedbackNotifications.Where(n => n.SubmissionId == submissionId).Select(n => n.State).SingleAsync(Ct));
    }

    [Fact]
    public async Task Every_route_answers_401_without_a_session()
    {
        var answers = await EveryRouteAsync(factory.CreateClient(), Guid.NewGuid());

        answers.Count.ShouldBe(6);
        answers.ShouldAllBe(answer => answer.Status == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_route_answers_403_to_an_account_without_the_admin_role_and_changes_nothing()
    {
        var (id, reporter) = await RequeueableAsync();

        var answers = await EveryRouteAsync(reporter.Client, id);

        answers.Count.ShouldBe(6);
        answers.ShouldAllBe(answer => answer.Status == HttpStatusCode.Forbidden);
        (await StoredAsync(id)).ShouldBe((FeedbackStatus.New, FeedbackNotificationState.Unknown));
    }

    [Fact]
    public async Task Every_route_answers_an_administrator_privately_and_unstored()
    {
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        var (id, _) = await RequeueableAsync();

        var answers = await EveryRouteAsync(admin, id);

        answers.Select(answer => answer.Status).ShouldBe(
        [
            HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
            HttpStatusCode.NoContent, HttpStatusCode.NoContent,
        ]);
        foreach (var answer in answers)
        {
            answer.CacheControl.ShouldNotBeNull(answer.Route).Private.ShouldBeTrue(answer.Route);
            answer.CacheControl.NoStore.ShouldBeTrue(answer.Route);
        }

        (await StoredAsync(id)).ShouldBe((FeedbackStatus.InProgress, FeedbackNotificationState.Queued));
    }
}
