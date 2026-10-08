using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;
using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackAvailability;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackScreenshot;
using Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;
using Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Feedback;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Feedback;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #1979 — feedback triage for administrators: the list, one item, the per-page summary, why the
/// feature is open or closed, a status change and a notice requeue. The list's filters are a status
/// and a page key, neither of which names a person, so they travel in the query string.
/// </summary>
public static class AdminFeedbackEndpoints
{
    public sealed record FeedbackStatusRequest(FeedbackStatus Status);

    public sealed record FeedbackRequeueRequest(bool AcknowledgeDuplicateRisk = false);

    public sealed record FeedbackAvailabilityResponse(FeedbackAvailability Availability);

    public static void MapAdminFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/feedback")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/", async (
            FeedbackStatus? status, string? page, int? pageNumber, int? pageSize,
            IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(await mediator.Send(
                new ListFeedbackQuery(status, page, pageNumber ?? 1, pageSize ?? 25), ct));
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet("/summary", async (int? days, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(await mediator.Send(new GetFeedbackSummaryQuery(days ?? 30), ct));
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet("/availability", async (IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var availability = await mediator.Send(new GetFeedbackAvailabilityQuery(), ct);
            return Results.Ok(new FeedbackAvailabilityResponse(availability));
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var detail = await mediator.Send(new GetFeedbackDetailQuery(id), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapGet("/{id:guid}/screenshot", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var screenshot = await mediator.Send(new GetFeedbackScreenshotQuery(id), ct);
            return screenshot is null
                ? DomainError.NotFound("Feedback.ScreenshotNotFound", "Det finns ingen skärmbild.").ToProblemResult()
                : Results.Bytes(screenshot.Content, FeedbackScreenshot.ContentType);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapPost("/{id:guid}/status", async (
            Guid id, FeedbackStatusRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new ChangeFeedbackStatusCommand(id, body.Status), ct);
            return result.IsFailure ? result.Error.ToProblemResult() : Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);

        group.MapPost("/{id:guid}/notification/requeue", async (
            Guid id, FeedbackRequeueRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(
                new RequeueFeedbackNotificationCommand(id, body.AcknowledgeDuplicateRisk), ct);
            return result.IsFailure ? result.Error.ToProblemResult() : Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);
    }
}
