using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;

/// <summary>
/// #2130 (ADR 0168) — the authenticated user opened ONE notification in the bell: mark that ad read in every
/// source (#2158, ADR 0173), in the bell and nowhere else. It sets the source rows' own
/// <c>ReadInNotificationFeed</c> flag and moves no watermark, so Översikt, <c>/matchningar</c> and
/// <c>/foretag/bevakade/nya</c> keep their numbers (Klas 2026-10-11); it is not the e-mail dedup stamp either.
///
/// <para>
/// <b>Owner-scoped, IDOR-safe (CLAUDE.md §5/§12):</b> the user is resolved in the handler from <c>ICurrentUser</c>,
/// never from the command, so a user can only mark their OWN rows. The non-PII <see cref="JobAdId"/> already sits in
/// the <c>/jobb/{id}</c> URL. A row that is missing, someone else's or already read, or a match whose consent is
/// withdrawn, changes nothing and is the same <c>Result.Success()</c>: the answer is 204 for every request from a
/// signed-in owner and never a NotFound, which would reveal whether the user has the row.
/// </para>
///
/// <para>
/// <b>Not audited (parity <c>MarkFollowedCompanyAdSeenCommand</c>):</b> a read mark is high-frequency behavioural
/// data with no security-relevant transition, and it touches no <c>JobSeeker</c>, so it is not a writer the replay guard
/// owns. It is an ordinary command.
/// </para>
/// </summary>
public sealed record MarkNotificationReadCommand(Guid JobAdId)
    : ICommand<Result>, IAuthenticatedRequest;
