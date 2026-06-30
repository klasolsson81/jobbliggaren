using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;
using Jobbliggaren.Application.CompanyWatches.Commands.UnfollowCompany;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — follow employers by org.nr. Route prefix
/// <c>/api/v1/me/company-watches</c> ("my data" convention).
///
/// <para>
/// <b>FORK D2 (surrogate resource):</b> POST takes the org.nr in the request BODY and returns the
/// <c>CompanyWatchId</c>; DELETE addresses the watch by that opaque id. The raw org.nr NEVER appears
/// in a URL path (and thus never in an access-log) — a sole-prop org.nr can equal a personnummer,
/// and a URL is logged un-flagged by infra we don't control (ADR 0087 D8(c), CLAUDE.md §5).
/// </para>
/// </summary>
public static class CompanyWatchesEndpoints
{
    public static void MapCompanyWatchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/company-watches")
            .WithTags("CompanyWatches")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListCompanyWatchesQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapPost("/", async (
            FollowCompanyCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            // Location bears the CompanyWatchId (Guid), never the org.nr (D8(c)).
            return result.IsSuccess
                ? Results.Created($"/api/v1/me/company-watches/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        group.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UnfollowCompanyCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}
