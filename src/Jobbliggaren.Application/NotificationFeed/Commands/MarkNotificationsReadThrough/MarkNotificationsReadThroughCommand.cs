using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationsReadThrough;

/// <summary>
/// #2130 (ADR 0162 as amended) — "Markera alla som lästa" for ONE source: mark every row of the source that is new
/// since its watermark, not yet read, and no newer than <see cref="Through"/>, the token the feed returned for it.
/// An explicit act over a number the user has seen. It writes the bell's own flags and moves NO watermark, so it no
/// longer clears Översikt's count or the lists' "new" marks (it did until #2130); the token bounds it exactly as it
/// bounds the count, so a row that arrived after the read stays unread.
///
/// <para>
/// Owner-scoped, benign and unaudited, as <c>MarkNotificationReadCommand</c> is. A <see cref="Through"/> after the
/// newest row marks no more than the user's own watermark-new rows, which are the rows the user can see counted.
/// </para>
/// </summary>
public sealed record MarkNotificationsReadThroughCommand(NotificationFeedSourceKind Kind, DateTimeOffset Through)
    : ICommand<Result>, IAuthenticatedRequest;
