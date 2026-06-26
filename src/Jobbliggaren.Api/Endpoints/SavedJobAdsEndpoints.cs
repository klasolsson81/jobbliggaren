using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd;
using Jobbliggaren.Application.SavedJobAds.Commands.UnsaveJobAd;
using Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// F6 P5 Punkt 2 Del A — SavedJobAds (bokmärkta annonser per JobSeeker).
/// Route-prefix <c>/api/v1/me/saved-job-ads</c> följer "my data"-konvention.
/// POST/DELETE använder <c>{jobAdId}</c> i path (inte SavedJobAdId) — det är
/// den naturliga semantiska nyckeln ("spara/ta bort *den här annonsen*"),
/// och SavedJobAdId är implementations-detalj.
/// </summary>
public static class SavedJobAdsEndpoints
{
    public static void MapSavedJobAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/saved-job-ads")
            .WithTags("SavedJobAds")
            .RequireAuthorization();

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ListSavedJobAdsQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapPost("/{jobAdId:guid}", async (
            Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SaveJobAdCommand(jobAdId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        group.MapDelete("/{jobAdId:guid}", async (
            Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UnsaveJobAdCommand(jobAdId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}
