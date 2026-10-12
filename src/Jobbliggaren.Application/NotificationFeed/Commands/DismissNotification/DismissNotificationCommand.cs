using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;

/// <summary>
/// #2151 (ADR 0169) — the authenticated user took ONE notification out of the bell with its X: that (source, ad) pair
/// stops being listed and, if it was unread, stops counting. It sets the source row's own bell flags and moves no
/// watermark and deletes nothing, so Översikt, <c>/matchningar</c> and <c>/foretag/bevakade/nya</c> keep their rows and
/// numbers (Klas 2026-10-11, #2138).
///
/// <para>
/// <b>Owner-scoped, IDOR-safe:</b> the user comes from <c>ICurrentUser</c> in the handler, never from the command. A
/// row that is missing, someone else's, already dismissed or ended, or a match whose consent is withdrawn, changes
/// nothing and is the same <c>Result.Success()</c>: 204 for every request from a signed-in owner, never a NotFound.
/// </para>
///
/// <para>
/// <b>Only a click sets it.</b> Nothing but the user's X writes the dismissed flag, and nothing ever clears it
/// (ADR 0169, V20). It is not audited and touches no <c>JobSeeker</c>, like <c>MarkNotificationReadCommand</c>.
/// </para>
/// </summary>
public sealed record DismissNotificationCommand(NotificationFeedSourceKind Kind, Guid JobAdId)
    : ICommand<Result>, IAuthenticatedRequest;
