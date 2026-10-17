using System.Text.Json.Serialization;
using Jobbliggaren.Api.Common;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedFollowedAds;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedMatches;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationFeedBacklogRead;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationsReadThrough;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// The notification bell (#1666, #2130). Route prefix <c>/api/v1/me/notification-feed</c> ("my data" convention, and
/// "notification-feed" because "notification" already names the e-mail consents under <c>/api/v1/me</c>). The user
/// comes from the session only; no route, query or body carries one, so there is no IDOR surface.
/// <list type="bullet">
/// <item>One GET with no parameters: the feed. It writes nothing.</item>
/// <item>Three POST marks: one notification read, and everything read through a token (#2130), and one notification
///   taken out of the bell with its X (#2151). They set the source rows' own bell flags (<c>read_in_notification_feed</c>; the X also
///   <c>dismissed_from_notification_feed</c>) and move no watermark. 204 for every request from a signed-in owner,
///   whether or not the row exists, is theirs, or is still listed; they share their own bucket.</item>
/// <item>Two PUT switches (#2130): whether a type reaches the bell. Delivery choices, never consents; each carries only
///   <c>{enabled}</c>. Switching ON first marks what arrived while the type was off as read, then clears the stamp.</item>
/// </list>
/// </summary>
public static class MeNotificationFeedEndpoints
{
    // The path segment of each source: a closed set, never a bare enum name or number from the wire. Anything else is a
    // 400 decided from the request alone, before any read.
    private static readonly Dictionary<string, NotificationFeedSourceKind> Sources =
        new Dictionary<string, NotificationFeedSourceKind>(StringComparer.Ordinal)
        {
            ["matches"] = NotificationFeedSourceKind.Match,
            ["followed-company-ads"] = NotificationFeedSourceKind.FollowedCompanyAd,
        };

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

        // #2130 - one notification read. The ad id (non-PII, already in /jobb/{id}) is in the path; the owner is not.
        // 204 on Success, also when nothing changes (a missing or foreign row, or a match whose consent is withdrawn):
        // never a 404, which would reveal whether the user has the row. A non-GUID id never matches the route. The
        // segment names the row the user acted on and stays a closed set; the mark reaches the ad in every source (#2158).
        group.MapPost("/{source}/{jobAdId:guid}/read", async (
            string source, Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            if (!Sources.ContainsKey(source))
                return UnknownSource();

            var result = await mediator.Send(new MarkNotificationReadCommand(jobAdId), ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.NotificationFeedMarkPolicy);

        // #2151 - the X on one notification: takes that ad out of the bell, in every source (#2158), nothing else. A POST
        // and not a DELETE: no row is deleted (#2138 forbids it), a flag is set. Same 204 for every signed-in owner.
        group.MapPost("/{source}/{jobAdId:guid}/dismiss", async (
            string source, Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            if (!Sources.ContainsKey(source))
                return UnknownSource();

            var result = await mediator.Send(new DismissNotificationCommand(jobAdId), ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.NotificationFeedMarkPolicy);

        // #2130 - "Markera alla som lästa" for one source: everything new, unread and no newer than the token the feed
        // returned for it. A missing or malformed token is a 400 before any read.
        group.MapPost("/{source}/read", async (
            string source, MarkNotificationsReadThroughRequest request, IMediator mediator, CancellationToken ct) =>
        {
            if (!Sources.TryGetValue(source, out var kind))
                return UnknownSource();

            var result = await mediator.Send(new MarkNotificationsReadThroughCommand(kind, request.ThroughToken), ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.NotificationFeedMarkPolicy);

        // #2130 - whether a type reaches the bell. MeWritePolicy: a switch is a user-owned mutation like the consents
        // and the mail pauses, and a switch-on is ONE request on this bucket, backlog mark included.
        group.MapPut("/matches", (
            SetNotificationFeedMatchesCommand command, IMediator mediator, CancellationToken ct) =>
            SwitchAsync(NotificationFeedSourceKind.Match, command.Enabled, () => mediator.Send(command, ct), mediator, ct))
            .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        group.MapPut("/followed-company-ads", (
            SetNotificationFeedFollowedAdsCommand command, IMediator mediator, CancellationToken ct) =>
            SwitchAsync(NotificationFeedSourceKind.FollowedCompanyAd, command.Enabled, () => mediator.Send(command, ct), mediator, ct))
            .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }

    /// <summary>
    /// A switch. Switching ON first marks what arrived while the type was off as read, then the replay-marked command
    /// clears the stamp: the stamp is the key to the backlog, so it must still be there when the backlog is marked.
    /// The mark is composed HERE and not inside the settings command because the writer-replay guard (ADR 0146)
    /// forbids a bulk write in a replayed handler. A failure between the two leaves the type off with its backlog
    /// read, which is harmless; one after a producer committed a row in between leaves that row unread, which is a new
    /// notification, not a flood. Switching OFF is the stamp alone.
    /// </summary>
    private static async Task<IResult> SwitchAsync(
        NotificationFeedSourceKind kind,
        bool enabled,
        Func<ValueTask<Result<Guid>>> set,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (enabled)
        {
            var marked = await mediator.Send(new MarkNotificationFeedBacklogReadCommand(kind), cancellationToken);
            if (marked.IsFailure)
                return marked.Error.ToProblemResult();
        }

        var result = await set();
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult();
    }

    private static IResult UnknownSource() =>
        DomainError.Validation("NotificationFeed.UnknownSource", "Okänd notiskälla.").ToProblemResult();
}

/// <summary>
/// The body of "Markera alla som lästa" for one source: the token the feed returned for it, as the original string
/// (the API stores microseconds). Required on the wire: a body without it is a 400, never an unbound default.
/// </summary>
public sealed record MarkNotificationsReadThroughRequest([property: JsonRequired] DateTimeOffset ThroughToken);
