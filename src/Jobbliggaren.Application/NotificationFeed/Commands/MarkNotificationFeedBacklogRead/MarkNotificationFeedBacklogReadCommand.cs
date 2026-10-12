using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationFeedBacklogRead;

/// <summary>
/// #2130 (ADR 0168) — what arrived while the user had a notification type switched off is marked read in
/// the bell, so switching the type back ON does not flood it. Sent by the switch-on endpoint BEFORE the command that
/// clears the off-stamp: the stamp is the key, so the order matters, and a failure between the two leaves the type
/// off with its backlog read, which is harmless (a retry marks nothing more).
///
/// <para>
/// It lives apart from the settings command because the writer-replay guard forbids a bulk write inside a replayed
/// <c>JobSeeker</c> command (ADR 0146), and it carries no stamp: it reads the user's own, so the request cannot
/// choose "since when". No stamp, which is "the type is on", marks nothing; <c>null</c> never means "since forever".
/// Owner-scoped, benign and unaudited, as <c>MarkNotificationReadCommand</c> is.
/// </para>
/// </summary>
public sealed record MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind Kind)
    : ICommand<Result>, IAuthenticatedRequest;
