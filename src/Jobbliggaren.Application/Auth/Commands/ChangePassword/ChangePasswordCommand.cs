using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Self-service change-password (#678, C5-password of epik #481). The re-authentication credential is a
/// purpose-scoped grant (#1739, ADR 0142 D5): <see cref="IReauthenticatingRequest"/> makes
/// <c>ReauthenticationBehavior</c> redeem it server-side (oracle-safe 401) BEFORE the handler runs, so a
/// hijacked long-lived session cannot change the password on its own. The CURRENT password travels beside
/// the grant because Identity's <c>ChangePasswordAsync</c> requires it; it is not the re-auth credential.
/// Neither password nor the grant ever reaches a log or audit projection. The surface goes with 5a
/// (epic #1732), which is why the third implementer of the marker is this one.
///
/// <para>
/// The re-issue of the current session + logout-everywhere (C6) is orchestrated by the endpoint
/// post-command (it owns <c>ISessionStore</c> and returns the new session id); the handler only
/// performs the Identity password change. Returns the authenticated user id so
/// <c>AuditBehavior</c> can stamp the <c>User.PasswordChanged</c> row (AggregateType = "User": the
/// credential lives on the Identity user, not the JobSeeker aggregate).
/// </para>
/// </summary>
public sealed record ChangePasswordCommand(string? ReauthGrant, string? CurrentPassword, string? NewPassword)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IReauthenticatingRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "User.PasswordChanged";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
