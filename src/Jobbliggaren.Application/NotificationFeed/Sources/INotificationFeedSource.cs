using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// One reason the bell can ring (#1666, ADR 0162). A source answers, under the single definition its own context
/// already owns, three things for one user: the exact unread count, the token that acknowledges exactly
/// that count, and its newest rows.
///
/// <para>
/// <b>The contract every source keeps:</b>
/// <list type="bullet">
/// <item>it reads EXISTING rows only — the bell has no store of its own and writes nothing on a read
///   (ADR 0018 amendment 2026-10-10: a write on a GET is a CSRF surface);</item>
/// <item>"new" is its context's own watermark and predicate, composed, never restated;</item>
/// <item>the token is the newest unread row's timestamp, read FIRST; the count and the list are bounded
///   by it, so the number shown is exactly what "Markera alla som lästa" acknowledges;</item>
/// <item>it is read under the legal ground its rows were produced under (a consent-produced source is
///   gated on that consent at read time);</item>
/// <item>it is stateless: one instance serves every request, and anything per request travels in
///   <see cref="NotificationFeedReadContext"/>.</item>
/// </list>
/// A new source is a new
/// data flow into a per-user surface — security-auditor reviews it before it ships.
/// </para>
/// </summary>
internal interface INotificationFeedSource
{
    NotificationFeedSourceKind Kind { get; }

    Task<NotificationSourceRead> ReadAsync(NotificationFeedReadContext context, CancellationToken cancellationToken);
}

/// <summary>Everything a source may need for one request, passed in so no source holds state.</summary>
internal sealed record NotificationFeedReadContext(
    IAppDbContext Db,
    Guid UserId,
    int MaxItems,
    IMatchProfileBuilder ProfileBuilder,
    IPerUserJobAdSearchQuery PerUserSearch);

/// <summary>
/// One source's answer. <see cref="Horizon"/> is set when the source read a WINDOW of rows that it
/// filters after reading: rows at or before it may be missing from <see cref="Items"/>, so the merge
/// shows no READ row that old from any source. An unread row is never cut by it: it is selected because
/// it is unread. <c>null</c> when <see cref="Items"/> is exactly the source's newest rows.
/// </summary>
internal sealed record NotificationSourceRead(
    NotificationFeedSourceKind Kind,
    int? UnreadCount,
    DateTimeOffset? AcknowledgedThrough,
    IReadOnlyList<NotificationFeedItemDto> Items,
    DateTimeOffset? Horizon)
{
    /// <summary>A source with nothing to say for this user: no rows, nothing unread, nothing to acknowledge.</summary>
    public static NotificationSourceRead Nothing(NotificationFeedSourceKind kind) =>
        new(kind, 0, null, [], null);
}
