using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmail;

/// <summary>
/// Registration email-confirmation — CONFIRM step (#714). Token-gated and PUBLIC: the activation link
/// is opened from the account's own inbox, possibly logged-out or on a different device, so this
/// command is deliberately NOT <c>IAuthenticatedRequest</c> and NOT <c>IReauthenticatingRequest</c> —
/// the opaque, SecurityStamp-bound token IS the authorization. Named "Verify…" so it does NOT trip the
/// re-auth tripwire (which targets Change/Update/Set/Reset + Email/Password/Credential and the erasure/
/// export families — never "Verify").
///
/// <para>
/// Sets <c>EmailConfirmed=true</c> (idempotent within the token lifespan — the stamp is not rotated).
/// Returns the target user id so <c>AuditBehavior</c> stamps <c>User.EmailConfirmed</c> (AggregateType
/// "User"); the actor is null when the confirmer is logged-out, which is correct (the token proves
/// authorization). Every rejection is ONE uniform failure so this public endpoint reveals no
/// account-existence or enumeration oracle. No logout-everywhere (not a recovery-vector change) and no
/// session is issued (the user then logs in).
/// </para>
/// </summary>
public sealed record VerifyEmailCommand(Guid Uid, string? Token)
    : ICommand<Result<Guid>>, IAuditableCommand<Result<Guid>>
{
    public string EventType => "User.EmailConfirmed";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
