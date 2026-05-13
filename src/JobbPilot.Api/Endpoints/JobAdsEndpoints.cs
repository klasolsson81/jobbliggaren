using JobbPilot.Api.RateLimiting;
using JobbPilot.Application.JobAds.Commands.CreateJobAd;
using JobbPilot.Application.JobAds.Queries.GetJobAd;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class JobAdsEndpoints
{
    public static void MapJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        // ADR 0005: JobAd-listning/sökning är auth-gated i Fas 2-start. Anonym
        // publik katalog kan låsas upp senare via separat ADR efter mätning av
        // JobTech-proxy-kostnad och bot-trafik.
        var group = app.MapGroup("/api/v1/job-ads")
            .WithTags("JobAds")
            .RequireAuthorization();

        // GET routes (list + by-id) skyddas med ListReadPolicy mot multi-query-
        // DoS från komprometterat konto via wildcard-LIKE-pattern (?q=%term%).
        // POST nedan har inte denna policy — admin-flöde med egen yta.
        // Per CTO-rond 2026-05-13 F2-P9 + security-auditor Major-fynd.
        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc,
            string? ssyk = null,
            string? region = null,
            string? q = null,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(
                new ListJobAdsQuery(page, pageSize, sortBy, ssyk, region, q), ct);
            return Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.ListReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetJobAdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireRateLimiting(RateLimitingExtensions.ListReadPolicy);

        group.MapPost("/", async (
            CreateJobAdCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/job-ads/{result.Value}", new { id = result.Value })
                : Results.Problem(
                    title: result.Error.Code,
                    detail: result.Error.Message,
                    statusCode: 400);
        });
    }
}
