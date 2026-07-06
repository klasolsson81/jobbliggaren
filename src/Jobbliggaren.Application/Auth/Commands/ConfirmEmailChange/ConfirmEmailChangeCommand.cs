using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

/// <summary>
/// Self-service change-email — CONFIRM step (#679). Token-gated and PUBLIC: the link is opened from
/// the NEW inbox, possibly logged-out or on a different device, so this command is deliberately NOT
/// <c>IAuthenticatedRequest</c> and NOT <c>IReauthenticatingRequest</c> — the opaque, single-use,
/// SecurityStamp-bound token IS the authorization. Named "Confirm…" so it does NOT trip the re-auth
/// tripwire (which targets Change/Update/Set/Reset + Email/Password/Credential, and the erasure/export
/// families — never "Confirm").
///
/// <para>
/// Applies the pending change (verify token → swap email → keep UserName in lockstep → rotate the
/// stamp); the endpoint then logs out every session (C6). Returns the target user id so
/// <c>AuditBehavior</c> stamps <c>User.EmailChanged</c> (AggregateType "User"); the actor is null when
/// the confirmer is logged-out, which is correct (the token proves authorization). Every rejection is
/// ONE uniform failure so this public endpoint reveals no account-existence or enumeration oracle.
/// </para>
/// </summary>
public sealed record ConfirmEmailChangeCommand(Guid UserId, string? NewEmail, string? Token)
    : ICommand<Result<Guid>>, IAuditableCommand<Result<Guid>>
{
    public string EventType => "User.EmailChanged";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
