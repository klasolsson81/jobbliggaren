using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;
using Jobbliggaren.Application.Common.Authorization;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The Server card's host readings (#1982): CPU, memory and disk of the host the stack runs on. The request
/// never touches the host; it reads the sampler's newest result from memory (ADR 0158). One call carries all
/// three readings, each with its own state, so one failing reading never blanks the others.
/// </summary>
public static class AdminHostObservationEndpoints
{
    public static void MapAdminHostObservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/overview/host", async (IMediator mediator, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetHostObservationQuery(), ct);
            // The API read instant, like every overview source. When each reading was taken is in the body.
            response.Headers[AdminEndpoints.SampledAtHeader] = result.ReadAt.ToString("O", CultureInfo.InvariantCulture);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);
    }
}
