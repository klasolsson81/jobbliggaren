using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The anonymous per-record read of one job ad (ADR 0160). Its own group, outside the
/// authenticated <c>/api/v1/job-ads</c>: anonymous by omitting <c>RequireAuthorization</c>, with
/// its own IP-partitioned policy.
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
