using Jobbliggaren.Domain.Matching;

namespace Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;

/// <summary>
/// The bell's payload (#1666). <see cref="Sources"/> carries one entry per source; the client's unread
/// total is their sum, and it renders no number at all when any entry's count is <c>null</c> (ADR 0120).
/// </summary>
public sealed record NotificationFeedDto(
    IReadOnlyList<NotificationFeedItemDto> Items,
    IReadOnlyList<NotificationFeedSourceDto> Sources)
{
    public static NotificationFeedDto Empty { get; } = new([], []);
}

/// <summary>
/// One notification: a (source, ad) pair. <see cref="Grade"/> is the stored match grade on a
/// <see cref="NotificationFeedSourceKind.Match"/> row and <c>null</c> otherwise. Carries no external
/// URL, organisation number, watch, evidence, score or delivery state: the row links to
/// <c>/jobb/{JobAdId}</c> and names its reason.
/// </summary>
public sealed record NotificationFeedItemDto(
    NotificationFeedSourceKind Kind,
    Guid JobAdId,
    string Title,
    string CompanyName,
    DateTimeOffset OccurredAt,
    bool IsUnread,
    NotifiableMatchGrade? Grade);

/// <summary>
/// One source's unread state. <see cref="UnreadCount"/> is exact, or <c>null</c> when the source
/// refused to count past its bound (ADR 0120 clause 5). <see cref="AcknowledgedThrough"/> is the newest
/// unread row's timestamp — the value "Markera alla som lästa" sends back to the source's existing
/// <c>/seen</c> endpoint unchanged — and <c>null</c> when nothing is unread.
/// </summary>
public sealed record NotificationFeedSourceDto(
    NotificationFeedSourceKind Kind,
    int? UnreadCount,
    DateTimeOffset? AcknowledgedThrough);
