using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Self-service change-password (#678, C5-password of epik #481). The CURRENT password is the
/// re-authentication credential: <see cref="IReauthenticatingRequest"/> makes
/// <c>ReauthenticationBehavior</c> verify it server-side (lockout-aware, oracle-safe 401) BEFORE
/// the handler runs, so a hijacked long-lived session cannot change the password without knowing
/// the current one. Neither password ever reaches a log or audit projection.
///
/// <para>
/// The re-issue of the current session + logout-everywhere (C6) is orchestrated by the endpoint
/// post-command (it owns <c>ISessionStore</c> and returns the new session id); the handler only
/// performs the Identity password change. Returns the authenticated user id so
/// <c>AuditBehavior</c> can stamp the <c>User.PasswordChanged</c> row (AggregateType = "User": the
/// credential lives on the Identity user, not the JobSeeker aggregate).
/// </para>
/// </summary>
public sealed record ChangePasswordCommand(string? CurrentPassword, string? NewPassword)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IReauthenticatingRequest, IAuditableCommand<Result<Guid>>
{
    // The current password is the re-auth credential verified by ReauthenticationBehavior.
    public string? Password => CurrentPassword;

    public string EventType => "User.PasswordChanged";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
