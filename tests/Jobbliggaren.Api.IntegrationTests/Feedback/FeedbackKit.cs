using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #1979 — signed-in reporters made by the production registrar, the multipart request the web sends, and reads of
/// what the submit path stored. Feedback is only ever written through <c>POST /api/v1/me/feedback</c> here, except
/// where a test names another actor.
/// </summary>
internal static class FeedbackKit
{
    public const string SubmitPath = "/api/v1/me/feedback";
    public const string PromptStatePath = "/api/v1/me/feedback/prompt-state";
    public const string AdminPath = "/api/v1/admin/feedback";
    public const string AppVersion = "c2c2c6cea";

    public static string DetailPath(Guid id) => $"{AdminPath}/{id}";

    public static string StatusPath(Guid id) => $"{AdminPath}/{id}/status";

    public static string RequeuePath(Guid id) => $"{AdminPath}/{id}/notification/requeue";

    public static string SummaryPath(int days) => $"{AdminPath}/summary?days={days}";

    /// <summary>A signed-in account with its job seeker, as the production registrar opens it.</summary>
    internal sealed record Reporter(HttpClient Client, Guid UserId, JobSeekerId JobSeekerId, string Email);

    public static async Task<Reporter> ReporterAsync(ApiFactory factory, CancellationToken ct)
    {
        var email = $"feedback-{Guid.NewGuid():N}@example.se";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: ct);
        var userId = await AdminAccountsKit.UserIdAsync(factory, email, ct);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        await using var scope = factory.Services.CreateAsyncScope();
        var jobSeekerId = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .SingleAsync(ct);
        return new Reporter(client, userId, jobSeekerId, email);
    }

    /// <summary>The JSON the web puts in the <c>payload</c> field.</summary>
    public static object Payload(Guid key, string page, int? rating = null, string? comment = null) => new
    {
        submissionKey = key,
        page,
        rating,
        comment,
        client = new
        {
            viewportWidth = 1280,
            viewportHeight = 720,
            screenWidth = 1920,
            screenHeight = 1080,
            pixelRatio = 1.5m,
            theme = "dark",
            deviceClass = "desktop",
            osFamily = "windows",
            browserFamily = "firefox",
        },
        appVersion = AppVersion,
    };

    public static MultipartFormDataContent Form(object payload) => new()
    {
        { new StringContent(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)), "payload" },
    };

    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, object payload, CancellationToken ct)
    {
        using var form = Form(payload);
        return await client.PostAsync(SubmitPath, form, ct);
    }

    /// <summary>The submit response's id and whether it replayed an earlier submission.</summary>
    public static async Task<(Guid Id, bool Replayed)> SubmittedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        using var body = JsonDocument.Parse(text);
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("replayed").GetBoolean());
    }

    /// <summary>A new submission through the real endpoint; its id.</summary>
    public static async Task<Guid> SubmitNewAsync(
        HttpClient client, string page, int? rating, string? comment, CancellationToken ct)
    {
        using var response = await SubmitAsync(client, Payload(Guid.NewGuid(), page, rating, comment), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await SubmittedAsync(response, ct)).Id;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }

    /// <summary>The ProblemDetails title, which carries the domain error's code.</summary>
    public static async Task<string?> ProblemTitleAsync(HttpResponseMessage response, CancellationToken ct) =>
        (await JsonAsync(response, ct)).GetProperty("title").GetString();

    internal sealed record Stored(int Submissions, int Notices, int Suppressions);

    public static async Task<Stored> StoredAsync(ApiFactory factory, JobSeekerId owner, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return new Stored(
            await db.FeedbackSubmissions.CountAsync(s => s.JobSeekerId == owner, ct),
            await db.FeedbackNotifications.CountAsync(n => n.JobSeekerId == owner, ct),
            await db.FeedbackPromptSuppressions.CountAsync(s => s.JobSeekerId == owner, ct));
    }

    /// <summary>
    /// A send whose outcome the provider could not prove, through <c>FeedbackNotificationDispatchJob</c>'s own
    /// transforms in its own order: the claim is saved before the provider call, then the unknown outcome. The Api
    /// does not host the job, and running it here would take the oldest due notice in the shared database, which need
    /// not be this one. <c>FeedbackNotificationDispatchJobTests.RunAsync_AnUnknownOutcome_IsNeverSentAgainAndStopsTheRun</c>
    /// pins the state the job leaves: Unknown after one attempt.
    /// </summary>
    public static async Task LoseTheOutcomeAsync(ApiFactory factory, Guid submissionId, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
        var notice = await db.FeedbackNotifications
            .SingleAsync(n => n.SubmissionId == new FeedbackSubmissionId(submissionId), ct);

        notice.Claim(now).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        notice.RecordUnknown(now).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }
}
