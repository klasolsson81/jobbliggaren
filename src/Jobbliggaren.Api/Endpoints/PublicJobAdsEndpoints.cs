using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The anonymous per-record read of one job ad (ADR 0160). Its own group, outside the
/// authenticated <c>/api/v1/job-ads</c>: anonymous by omitting <c>RequireAuthorization</c>, with
/// its own IP-partitioned policy. Every answer under <c>/api/v1/public</c> is <c>no-store</c>
/// (set in <c>Program.cs</c>, so framework refusals carry it too): an erased ad must never be
/// served from a cache.
/// </summary>
public static class PublicJobAdsEndpoints
{
    public static void MapPublicJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/public/job-ads").WithTags("PublicJobAds");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetPublicJobAdQuery(id), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemResult();
            })
            .RequireRateLimiting(RateLimitingExtensions.PublicJobAdReadPolicy);
    }
}
