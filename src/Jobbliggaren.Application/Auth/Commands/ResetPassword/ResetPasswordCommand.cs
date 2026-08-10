using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ResetPassword;

/// <summary>
/// #1171 — applies a password reset against an emailed one-time token. PUBLIC: the token IS the
/// authorization, which is the whole point of a recovery path (the actor has no session and no current
/// password to re-authenticate with).
/// <para>
/// <b>On the name, because it trips a tripwire's regex and a reviewer will notice.</b>
/// <c>ReauthenticationTripwireTests</c> matches <c>(Change|Update|Set|Reset)(Email|Password|Credential)</c>
/// and requires a match to also implement <c>IReauthenticatingRequest</c> — but its population is
/// <c>IAuthenticatedRequest</c> implementors, and this command is deliberately not one. It is the honest
/// name, it mirrors <c>IUserAccountService.ResetPasswordAsync</c> and Identity's own API, and the failure
/// mode is beneficial: anyone later adding <c>IAuthenticatedRequest</c> to it gets a red build demanding
/// re-authentication, which is impossible for a token-gated command and is exactly the right stop signal.
/// The negative is pinned by a test rather than left to this paragraph. Same escape the sibling
/// <c>ConfirmEmailChangeCommand</c> and <c>VerifyEmailCommand</c> names take.
/// </para>
/// <para>
/// <b>Auditable, unlike the request half.</b> A completed credential mutation with a known user id, exact
/// parity with <c>User.EmailChanged</c> and <c>User.PasswordChanged</c>. <c>AuditFailures</c> stays default
/// <c>false</c>: a rejected token has no aggregate id, and stamping failures would re-open the enumeration
/// oracle the uniform token rejection exists to close.
/// </para>
/// </summary>
public sealed record ResetPasswordCommand(Guid UserId, string? Token, string? NewPassword)
    : ICommand<Result<Guid>>, IAuditableCommand<Result<Guid>>
{
    public string EventType => "User.PasswordReset";

    public string AggregateType => "User";

    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
