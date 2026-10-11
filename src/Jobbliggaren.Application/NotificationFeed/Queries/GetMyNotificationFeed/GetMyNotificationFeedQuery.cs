using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;

/// <summary>
/// The signed-in user's notification bell (#1666): the newest notifications from every source, and per
/// source the exact unread count and the token that acknowledges it. Parameterless: the feed is the
/// authenticated user's own, and the user comes from <c>ICurrentUser</c> only. Capped, not paginated
/// (the response carries per-source tokens, which <c>PagedResult</c> has nowhere to put).
/// </summary>
public sealed record GetMyNotificationFeedQuery : IQuery<NotificationFeedDto>;
