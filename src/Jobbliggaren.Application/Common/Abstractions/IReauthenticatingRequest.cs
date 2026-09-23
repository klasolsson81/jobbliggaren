namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Marks a sensitive command whose acting user must re-authenticate before it runs (C5, epik #481;
/// a purpose-scoped grant since #1739, ADR 0142 D5). <c>ReauthenticationBehavior</c> enforces it in
/// the Mediator pipeline (after authorization, before the DEK-prefetch / UnitOfWork / Audit stages):
/// a failed re-auth throws
/// <see cref="Jobbliggaren.Application.Common.Exceptions.ReauthenticationFailedException"/> — mapped
/// centrally to a byte-identical <c>Auth.InvalidCredentials</c> 401 — and the handler never runs
/// (no side-effect, no audit row).
///
/// Server-side enforcement is the point: a hijacked long-lived session (persistent login is
/// 180d) must not be able to run a sensitive operation on its own, even by calling the endpoint
/// directly. The grant is earned by presenting the code mailed to the account's own address
/// (<c>VerifyReauthenticationChallengeCommand</c>), travels WITH the operation, is single-use, and
/// is never logged (<c>LoggingBehavior</c> logs only the message type name; <c>AuditBehavior</c>
/// emits only the <c>IAuditableCommand</c> projection). An architecture tripwire requires every
/// sensitive Auth command to implement this marker, so a future change-email / change-password /
/// export cannot ship without re-authentication.
/// </summary>
public interface IReauthenticatingRequest
{
    string? ReauthGrant { get; }
}
