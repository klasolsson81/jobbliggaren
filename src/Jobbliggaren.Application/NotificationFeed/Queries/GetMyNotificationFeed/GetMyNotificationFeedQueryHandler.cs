using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;

/// <summary>
/// The notification bell (#1666): a read-only union of <see cref="NotificationFeedSources.All"/>.
/// Owner-scoped through <see cref="ICurrentUser"/>; no authenticated user gives an empty feed. Writes
/// nothing — acknowledgement goes through each source's existing <c>/seen</c> command, never a read.
///
/// <para>
/// The sources run one after another because they share one scoped <see cref="IAppDbContext"/>, which
/// is not safe for concurrent use. The merge keeps one row per (source, ad). It selects every unread
/// row first, up to the cap, and fills the rest with the newest read rows above the newest horizon a
/// windowed source reported (see <see cref="NotificationSourceRead.Horizon"/>), so no read row can take
/// the place of an unread one. The selection is shown newest first, ties broken by kind and then ad id.
/// </para>
/// </summary>
public sealed class GetMyNotificationFeedQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<GetMyNotificationFeedQuery, NotificationFeedDto>
{
    /// <summary>The rows the panel shows. The full lists live on <c>/matchningar</c> and <c>/foretag/bevakade/nya</c>.</summary>
    public const int MaxItems = 20;

    public async ValueTask<NotificationFeedDto> Handle(
        GetMyNotificationFeedQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return NotificationFeedDto.Empty;

        var context = new NotificationFeedReadContext(db, userId, MaxItems, profileBuilder, perUserSearch);

        var reads = new List<NotificationSourceRead>(NotificationFeedSources.All.Count);
        foreach (var source in NotificationFeedSources.All)
            reads.Add(await source.ReadAsync(context, cancellationToken));

        return Merge(reads, MaxItems);
    }

    internal static NotificationFeedDto Merge(IReadOnlyList<NotificationSourceRead> reads, int maxItems)
    {
        var horizon = reads.Max(r => r.Horizon);
        var all = reads.SelectMany(r => r.Items).ToList();

        // A row is selected BECAUSE it is unread, so the horizon never cuts one: the badge counts it, and
        // the panel has no other way to reach it. Read rows only fill what is left, and only where every
        // source has read them without a gap (above the newest horizon).
        var unread = ByTime(all.Where(i => i.IsUnread)).Take(maxItems).ToList();
        var read = ByTime(all.Where(i => !i.IsUnread && (horizon is not { } cut || i.OccurredAt > cut)))
            .Take(maxItems - unread.Count);

        var items = ByTime(unread.Concat(read)).ToList();

        var sources = reads
            .Select(r => new NotificationFeedSourceDto(r.Kind, r.UnreadCount, r.AcknowledgedThrough))
            .ToList();

        return new NotificationFeedDto(items, sources);
    }

    /// <summary>Newest first, ties broken by kind and then ad id: a row never moves when its read state changes.</summary>
    private static IOrderedEnumerable<NotificationFeedItemDto> ByTime(IEnumerable<NotificationFeedItemDto> items) =>
        items.OrderByDescending(i => i.OccurredAt).ThenBy(i => i.Kind).ThenBy(i => i.JobAdId);
}
