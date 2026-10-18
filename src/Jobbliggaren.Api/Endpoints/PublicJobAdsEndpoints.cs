using System.Diagnostics;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The anonymous reads of job ads: one ad (ADR 0160) and the ids of one job-sitemap part (ADR 0174). Their own
/// group, outside the authenticated <c>/api/v1/job-ads</c>: anonymous by omitting <c>RequireAuthorization</c>, each
/// with its own IP-partitioned policy.
/// </summary>
public static class PublicJobAdsEndpoints
{
    /// <summary>The snapshot is read every five minutes, so a minute is a short, honest wait.</summary>
    private const string ListedIdsRetryAfterSeconds = "60";

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
                        http.Response.Headers.RetryAfter = ListedIdsRetryAfterSeconds;
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    default:
                        throw new UnreachableException($"Unmapped listed-ids answer {answer.GetType().Name}.");
                }
            })
            .RequireRateLimiting(RateLimitingExtensions.PublicJobAdListedIdsPolicy);
    }
}
