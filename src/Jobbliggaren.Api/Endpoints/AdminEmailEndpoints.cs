using System.Globalization;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// This month's mail against the provider's free allowance, and the computed cost beyond it (#1981, ADR 0178). The
/// e-post page and the overview's e-post card both read this one endpoint, so they share one definition.
/// </summary>
public static class AdminEmailEndpoints
{
    public const string AllowancePath = "/email/allowance";

    public static void MapAdminEmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet(AllowancePath, async (
            IMediator mediator, IDateTimeProvider clock, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetEmailAllowanceQuery(), ct);
            // The API read instant, like every overview source. When the provider answered is Usage.ObservedAt.
            response.Headers[AdminEndpoints.SampledAtHeader] =
                clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.AdminReadPolicy);
    }
}
