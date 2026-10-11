using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #1666 — the notification bell's read. Route prefix <c>/api/v1/me/notification-feed</c> ("my data"
/// convention, and "notification-feed" because "notification" already names the e-mail consents under
/// <c>/api/v1/me</c>). One GET with no route or query parameters: the user comes from the session only.
/// It writes nothing; the bell acknowledges through the sources' existing <c>/seen</c> endpoints.
/// </summary>
public static class MeNotificationFeedEndpoints
{
    public static void MapMeNotificationFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/notification-feed")
            .WithTags("Me")
            .RequireAuthorization();

        // private, no-store: the payload is per user and carries stored match grades (parity
        // MeFollowedCompanyAdsEndpoints). 200 { items, sources }.
        group.MapGet("/", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetMyNotificationFeedQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.NotificationFeedReadPolicy);
    }
}
