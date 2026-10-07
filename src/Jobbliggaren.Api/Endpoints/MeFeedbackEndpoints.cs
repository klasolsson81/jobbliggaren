using System.Text.Json;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;
using Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.AspNetCore.Http.Features;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #1979 — a signed-in user's feedback. The submission is multipart from the start: one JSON
/// <c>payload</c> field now, and the optional screenshot part in the next PR, so the contract keeps
/// its shape (senior-cto-advisor 2d). Parsing stays here; the command takes typed values (M4).
/// </summary>
public static class MeFeedbackEndpoints
{
    /// <summary>The whole multipart body today: the payload holds at most 2 000 characters of text.</summary>
    internal const long MaxSubmissionBytes = 64 * 1024;

    public sealed record FeedbackClientPayload(
        int? ViewportWidth = null,
        int? ViewportHeight = null,
        int? ScreenWidth = null,
        int? ScreenHeight = null,
        decimal? PixelRatio = null,
        string? Theme = null,
        string? DeviceClass = null,
        string? OsFamily = null,
        string? BrowserFamily = null);

    public sealed record FeedbackSubmissionPayload(
        Guid SubmissionKey,
        string? Page = null,
        int? Rating = null,
        string? Comment = null,
        FeedbackClientPayload? Client = null,
        string? AppVersion = null)
    {
        public override string ToString() =>
            $"FeedbackSubmissionPayload({SubmissionKey}, {Page}, comment {(Comment is null ? "none" : "redacted")})";
    }

    public sealed record FeedbackSubmissionResponse(Guid Id, bool Replayed);

    public static void MapMeFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/feedback").WithTags("Me");

        group.MapPost("/", async (HttpRequest request, IMediator mediator, CancellationToken ct) =>
        {
            var bodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
                bodySize.MaxRequestBodySize = MaxSubmissionBytes;

            if (!request.HasFormContentType)
                return InvalidSubmission();

            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = MaxSubmissionBytes,
                ValueCountLimit = 4,
                ValueLengthLimit = (int)MaxSubmissionBytes,
            }));

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return InvalidSubmission();
            }

            // A file part arrives with the screenshot support; until then it is refused, never ignored.
            if (form.Files.Count > 0)
                return InvalidSubmission();

            FeedbackSubmissionPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<FeedbackSubmissionPayload>(
                    form["payload"].ToString(), JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                return InvalidSubmission();
            }

            if (payload is null)
                return InvalidSubmission();

            var result = await mediator.Send(ToCommand(payload), ct);
            if (result.IsFailure)
                return result.Error.ToProblemResult();

            var response = new FeedbackSubmissionResponse(result.Value.FeedbackId, result.Value.Replayed);
            return result.Value.Replayed
                ? Results.Ok(response)
                : Results.Created((string?)null, response);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.FeedbackSubmitPolicy);

        group.MapGet("/prompt-state", async (IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(await mediator.Send(new GetFeedbackPromptStateQuery(), ct));
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.FeedbackPromptStatePolicy);
    }

    private static SubmitFeedbackCommand ToCommand(FeedbackSubmissionPayload payload)
    {
        var client = payload.Client ?? new FeedbackClientPayload();
        return new SubmitFeedbackCommand(
            payload.SubmissionKey,
            payload.Page,
            payload.Rating,
            payload.Comment,
            new ReportedClient(
                client.ViewportWidth,
                client.ViewportHeight,
                client.ScreenWidth,
                client.ScreenHeight,
                client.PixelRatio,
                ReportedName<ReportedTheme>(client.Theme),
                ReportedName<ReportedDeviceClass>(client.DeviceClass),
                ReportedName<ReportedOsFamily>(client.OsFamily),
                ReportedName<ReportedBrowserFamily>(client.BrowserFamily)),
            payload.AppVersion);
    }

    // A reported family is matched by NAME only; a number or an unknown name is dropped, never refused.
    private static T? ReportedName<T>(string? name) where T : struct, Enum =>
        !string.IsNullOrEmpty(name) && !char.IsAsciiDigit(name[0]) && !name.StartsWith('-')
            && Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : null;

    private static IResult InvalidSubmission() =>
        DomainError.Validation("Feedback.InvalidSubmission", "Feedbacken kunde inte läsas.").ToProblemResult();
}
