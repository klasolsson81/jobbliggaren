namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The bell's sources, in read order (#1666), which is also their precedence (ADR 0173 D7, ADR 0175 D8): match, then a
/// followed company's ad, then a Branschbevakning's. A lower source leaves out the ads a higher one holds; no higher
/// source reads a lower one. A static list rather than DI registrations: the handler is generator-registered and public,
/// so it cannot take an internal interface, and the sources need no composition-root edit.
/// <c>NotificationFeedSourcesTests</c> fails the build when a source class is missing here, listed twice, shares a kind,
/// holds instance state, or stands in another order than <see cref="NotificationFeedSourceKind"/>.
/// <para>
/// A source is also its own marker (#2130): <see cref="MarkerFor"/> resolves the write side of a source by its
/// kind, for "Markera alla" and the switch-on backlog; a click and an X reach every one of <see cref="Markers"/>
/// (#2158). The same instances serve both lists.
/// </para>
/// </summary>
internal static class NotificationFeedSources
{
    private static readonly MatchFeedSource Match = new();
    private static readonly FollowedCompanyAdFeedSource FollowedAds = new();
    private static readonly CompanyWatchCriterionAdFeedSource CriterionAds = new();

    public static IReadOnlyList<INotificationFeedSource> All { get; } = [Match, FollowedAds, CriterionAds];

    public static IReadOnlyList<INotificationFeedReadMarker> Markers { get; } = [Match, FollowedAds, CriterionAds];

    /// <summary>The write side of the source of <paramref name="kind"/>. Every kind has exactly one.</summary>
    public static INotificationFeedReadMarker MarkerFor(NotificationFeedSourceKind kind) =>
        Markers.Single(marker => marker.Kind == kind);
}
