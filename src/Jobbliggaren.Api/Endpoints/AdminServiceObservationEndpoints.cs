using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;
using Jobbliggaren.Application.Common.Authorization;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The services card's observations (#1982): the Worker's sign of life, the volatile Redis and the newest recorded
/// stream-import run. The request never touches a source; it reads the sampler's newest result from memory and
/// judges it against the API clock (ADR 0159). One call carries all three rows, each with its own state, so one
/// failing source never blanks the others.
/// </summary>
public static class AdminServiceObservationEndpoints
{
    public static void MapAdminServiceObservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/overview/services", async (IMediator mediator, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetServiceObservationQuery(), ct);
            // The API read instant, like every overview source. When each row was sampled is in the body.
            response.Headers[AdminEndpoints.SampledAtHeader] = result.ReadAt.ToString("O", CultureInfo.InvariantCulture);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);
    }
}
