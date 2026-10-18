using System.Diagnostics;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The anonymous reads of job ads: one ad (ADR 0160), the ids of one job-sitemap part (ADR 0174) and one page of the
/// public catalogue (ADR 0175). Their own group, outside the authenticated <c>/api/v1/job-ads</c>: anonymous by omitting
/// <c>RequireAuthorization</c>, each with its own IP-partitioned policy.
/// </summary>
public static class PublicJobAdsEndpoints
{
    /// <summary>The snapshot is read every five minutes, so a minute is a short, honest wait.</summary>
    private const string SnapshotRetryAfterSeconds = "60";

    public static void MapPublicJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/public/job-ads").WithTags("PublicJobAds");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetPublicJobAdQuery(id), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemResult();
            })
            .RequireRateLimiting(RateLimitingExtensions.PublicJobAdReadPolicy);

        // A 503 is the availability axis, which the ErrorKind union does not model (AGENTS.md §3, like the 401 arm):
        // the snapshot is missing or outside its bound, and a crawler must retry rather than read a removal.
        group.MapGet("/listed-ids", async (int part, int parts, HttpContext http, IMediator mediator, CancellationToken ct) =>
            {
                var answer = await mediator.Send(new GetListedJobAdIdsQuery(part, parts), ct);
                switch (answer)
                {
                    case ListedJobAdPart.Available available:
                        return Results.Ok(new ListedJobAdIdsDto(available.Ids));
                    case ListedJobAdPart.Unavailable:
                        http.Response.Headers.RetryAfter = SnapshotRetryAfterSeconds;
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    default:
                        throw new UnreachableException($"Unmapped listed-ids answer {answer.GetType().Name}.");
                }
            })
            .RequireRateLimiting(RateLimitingExtensions.PublicJobAdListedIdsPolicy);

        // A page past the end is a 404: the list moves, and a crawler must drop that address. A snapshot outside its
        // bound is the availability axis's 503, as for the sitemap.
        group.MapGet("/catalogue", async (int page, HttpContext http, IMediator mediator, CancellationToken ct) =>
            {
                var answer = await mediator.Send(new GetPublicJobAdCatalogueQuery(page), ct);
                switch (answer)
                {
                    case PublicJobAdCataloguePage.Available available:
                        return Results.Ok(available.Rows);
                    case PublicJobAdCataloguePage.NoSuchPage:
                        return Results.NotFound();
                    case PublicJobAdCataloguePage.Unavailable:
                        http.Response.Headers.RetryAfter = SnapshotRetryAfterSeconds;
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    default:
                        throw new UnreachableException($"Unmapped catalogue answer {answer.GetType().Name}.");
                }
            })
            .RequireRateLimiting(RateLimitingExtensions.PublicJobAdCataloguePolicy);
    }
}
