using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

/// <summary>
/// Self-service change-email — CONFIRM step (#679; a grant since #1739, ADR 0142 D5). Authenticated: the grant
/// <c>VerifyEmailChangeChallengeCommand</c> issued is redeemed for exactly this user and this address, and only
/// then is the account moved. It is NOT <c>IReauthenticatingRequest</c>: the re-authentication was the request
/// step's, and the proof of the new inbox is the grant. Named "Confirm…" so the re-auth tripwire's pattern does
/// not match it; a source sweep pins who may name the swap instead. Returns the user id so <c>AuditBehavior</c>
/// stamps <c>User.EmailChanged</c> (AggregateType "User"); the endpoint then logs every session out and issues
/// this device a fresh one.
/// </summary>
public sealed record ConfirmEmailChangeCommand(string? ChangeEmailGrant, string? NewEmail)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "User.EmailChanged";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
