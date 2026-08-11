using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;

/// <summary>
/// #1171 — request a password-reset link. Until this existed a user who forgot their password was
/// permanently locked out: <c>/change-password</c> requires a session, so it is the "I know my password"
/// path, not recovery, and the only remedy was editing the database by hand.
/// <para>
/// Resolves to a uniform 202 for a known address, an unknown address and a cooled repeat alike. A
/// malformed email is the only 400, and that is existence-INDEPENDENT so it is not an oracle. The reset
/// link is the only out-of-band signal, delivered only to an inbox the requester controls — the same
/// shape #733's resend uses.
/// </para>
/// <para>
/// <b>The one non-202 outcome is a 503 when no configured sender can deliver</b>
/// (<c>Auth.EmailDeliveryUnavailable</c>), and it is not an oracle because of WHERE it is decided: the
/// capability check is the handler's first statement and reads no input at all, so the 503/202 split is a
/// property of the server, evaluated before the submitted address is looked at. Placed after the account
/// lookup it would be reachable only for existing accounts and would disclose exactly what the uniform
/// 202 exists to hide.
/// </para>
/// <para>
/// <b>Deliberately NOT <c>IAuditableCommand</c>.</b> <c>AuditBehavior</c> stamps on <c>Result.Success</c>,
/// which this returns for every well-formed address, and an auditable command needs an aggregate id that
/// exists only for a real account — so <c>audit_log</c> would gain a row for existing addresses and none
/// for unknown ones, making the audit table an enumeration oracle for anyone who can read it. The send
/// branch calls <c>IAuthAuditLogger.PasswordResetRequested</c> directly instead. The COMPLETED reset is
/// auditable; the request is not.
/// </para>
/// </summary>
public sealed record RequestPasswordResetCommand(string? Email) : ICommand<Result>;
