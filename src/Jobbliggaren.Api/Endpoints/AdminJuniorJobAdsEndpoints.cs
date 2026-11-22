using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;
using Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #2264 (ADR 0187) — the admin junior queue: the listed ads in an enabled field that the rules left unclear, the ones
/// on <c>/junior</c> now, and the ones an admin has decided; and the decision itself. The list's filters are a tab and a
/// page, neither of which names a person, so they travel in the query string.
/// </summary>
public static class AdminJuniorJobAdsEndpoints
{
    /// <summary>A decision, or <see langword="null"/> to hand the ad back to the rules.</summary>
    public sealed record JuniorDecisionRequest(JuniorDecision? Decision);

    public static void MapAdminJuniorJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/junior-job-ads")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/", async (
            JuniorQueueView? view, int? page, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(await mediator.Send(
                new ListJuniorJobAdsQuery(view ?? JuniorQueueView.Unclear, page ?? 1), ct));
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);

        group.MapPost("/{id:guid}/decision", async (
            Guid id, JuniorDecisionRequest body, IMediator mediator, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new DecideJuniorJobAdCommand(id, body.Decision), ct);
            return result.IsFailure ? result.Error.ToProblemResult() : Results.NoContent();
        }).RequireRateLimiting(RateLimitingExtensions.AdminWritePolicy);
    }
}
