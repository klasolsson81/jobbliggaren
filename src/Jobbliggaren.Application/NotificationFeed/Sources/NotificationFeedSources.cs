namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The bell's sources, in read order (#1666). A static list rather than DI registrations: the handler is
/// generator-registered and public, so it cannot take an internal interface, and the sources need no
/// composition-root edit. <c>NotificationFeedSourcesTests</c> fails the build when a source class is
/// missing here, listed twice, shares a kind, or holds instance state.
/// </summary>
internal static class NotificationFeedSources
{
    public static IReadOnlyList<INotificationFeedSource> All { get; } =
    [
        new MatchFeedSource(),
        new FollowedCompanyAdFeedSource(),
    ];
}
