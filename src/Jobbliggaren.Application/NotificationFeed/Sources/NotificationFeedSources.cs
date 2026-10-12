namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The bell's sources, in read order (#1666). A static list rather than DI registrations: the handler is
/// generator-registered and public, so it cannot take an internal interface, and the sources need no
/// composition-root edit. <c>NotificationFeedSourcesTests</c> fails the build when a source class is
/// missing here, listed twice, shares a kind, or holds instance state.
/// <para>
/// A source is also its own marker (#2130): <see cref="MarkerFor"/> resolves the write side of a source by its
/// kind, for the commands that mark rows read. The same instances serve both lists.
/// </para>
/// </summary>
internal static class NotificationFeedSources
{
    private static readonly MatchFeedSource Match = new();
    private static readonly FollowedCompanyAdFeedSource FollowedAds = new();

    public static IReadOnlyList<INotificationFeedSource> All { get; } = [Match, FollowedAds];

    public static IReadOnlyList<INotificationFeedReadMarker> Markers { get; } = [Match, FollowedAds];

    /// <summary>The write side of the source of <paramref name="kind"/>. Every kind has exactly one.</summary>
    public static INotificationFeedReadMarker MarkerFor(NotificationFeedSourceKind kind) =>
        Markers.Single(marker => marker.Kind == kind);
}
