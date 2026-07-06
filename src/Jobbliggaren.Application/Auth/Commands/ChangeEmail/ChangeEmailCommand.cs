using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// Self-service change-email — REQUEST step (#679, C5-email of epik #481). The CURRENT password is
/// the re-authentication credential: <see cref="IReauthenticatingRequest"/> makes
/// <c>ReauthenticationBehavior</c> verify it server-side (lockout-aware, oracle-safe 401) BEFORE the
/// handler runs, so a hijacked long-lived session cannot repoint the account-recovery vector without
/// knowing the current password. The architecture tripwire (<c>ReauthenticationTripwireTests</c>)
/// forces this marker + a validator on any <c>ChangeEmail…</c> command.
///
/// <para>
/// This step does NOT change the email and does NOT touch sessions: it mints an opaque, URL-safe
/// ownership-confirmation token bound to (user, new email) and emails a confirmation link to the NEW
/// address. The swap happens only once the new owner opens the link (see
/// <c>ConfirmEmailChangeCommand</c>). Returns the authenticated user id so <c>AuditBehavior</c> can
/// stamp the <c>User.EmailChangeRequested</c> row (AggregateType = "User": the credential/email lives
/// on the Identity user, not the JobSeeker aggregate). Neither the password nor the new email ever
/// reaches a log or audit projection.
/// </para>
/// </summary>
public sealed record ChangeEmailCommand(string? CurrentPassword, string? NewEmail)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IReauthenticatingRequest, IAuditableCommand<Result<Guid>>
{
    // The current password is the re-auth credential verified by ReauthenticationBehavior.
    public string? Password => CurrentPassword;

    public string EventType => "User.EmailChangeRequested";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
