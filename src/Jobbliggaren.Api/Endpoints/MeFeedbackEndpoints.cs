using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;
using Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;
using Microsoft.AspNetCore.Http.Features;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>A signed-in user sends one feedback payload and an optional screenshot.</summary>
public static class MeFeedbackEndpoints
{
    internal const int MaxPayloadBytes = 64 * 1024;
    internal const long MaxSubmissionBytes = FeedbackScreenshot.MaxContentBytes + MaxPayloadBytes;

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

            if (request.ContentLength > MaxSubmissionBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            if (!request.HasFormContentType)
                return InvalidSubmission();

            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = MaxSubmissionBytes,
                MemoryBufferThreshold = (int)MaxSubmissionBytes,
                BufferBodyLengthLimit = MaxSubmissionBytes,
                ValueCountLimit = 2,
                ValueLengthLimit = MaxPayloadBytes,
            }));

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(ct);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return InvalidSubmission();
            }

            if (form.Count != 1 || !form.TryGetValue("payload", out var values) || values.Count != 1
                || form.Files.Count > 1 || (form.Files.Count == 1 && form.Files[0].Name != "screenshot"))
                return InvalidSubmission();

            var payloadJson = values[0] ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
                return InvalidSubmission();

            FeedbackSubmissionPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<FeedbackSubmissionPayload>(
                    payloadJson, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                return InvalidSubmission();
            }

            if (payload is null)
                return InvalidSubmission();

            ReadOnlyMemory<byte>? screenshot = null;
            if (form.Files.Count == 1)
            {
                var file = form.Files[0];
                if (file.Length <= 0 || file.Length > FeedbackScreenshot.MaxContentBytes)
                    return InvalidSubmission();
                var bytes = new byte[(int)file.Length];
                await using var stream = file.OpenReadStream();
                await stream.ReadExactlyAsync(bytes, ct);
                screenshot = bytes;
            }

            var result = await mediator.Send(ToCommand(payload, screenshot), ct);
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

    private static SubmitFeedbackCommand ToCommand(FeedbackSubmissionPayload payload, ReadOnlyMemory<byte>? screenshot)
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
            payload.AppVersion,
            screenshot);
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
