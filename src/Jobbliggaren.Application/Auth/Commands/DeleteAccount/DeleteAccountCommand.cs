using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

/// <summary>
/// GDPR Art. 17 — Right to erasure. Soft-deletar JobSeeker-aggregatet och alla
/// user-ägda Application + Resume-aggregat i samma SaveChanges (atomic via
/// UnitOfWorkBehavior). Endast EN audit-rad skrivs (Account.Deleted) — cascade
/// är persistence-detalj per ADR 0024 D4.
///
/// Idempotent: om kontot redan är soft-deletat returneras Success utan ny audit-rad
/// (handler returnerar tidigt innan SoftDelete-cascaden anropas igen).
///
/// Hard-delete + Identity-DELETE + audit-anonymisering sker av HardDeleteAccountsJob
/// efter 30-dagars restore-fönster (ADR 0024 D5+D6).
///
/// Anropas från POST /me/delete-endpoint. Kräver re-autentisering (lösenord):
/// <c>IReauthenticatingRequest</c> gör att <c>ReauthenticationBehavior</c> verifierar lösenordet
/// server-side FÖRE handlern körs (C5, epik #481) — en kapad long-lived session kan alltså inte
/// radera kontot utan lösenordet. Lösenordet når aldrig handlern och loggas aldrig. Endpoint
/// ansvarar för <c>ISessionStore.MarkUserDeletedAsync</c> + <c>InvalidateAllForUserAsync</c>
/// post-commit för att avsluta alla aktiva sessioner.
/// </summary>
public sealed record DeleteAccountCommand(string? Password)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IReauthenticatingRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Account.Deleted";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
