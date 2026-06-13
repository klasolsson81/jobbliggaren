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
/// Anropas från DELETE /me-endpoint. Endpoint ansvarar för
/// <c>ISessionStore.InvalidateAllForUserAsync</c>-anrop post-commit för att
/// avsluta alla aktiva sessioner.
/// </summary>
public sealed record DeleteAccountCommand
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Account.Deleted";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
