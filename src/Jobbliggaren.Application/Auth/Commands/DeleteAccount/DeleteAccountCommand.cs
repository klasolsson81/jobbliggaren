using Jobbliggaren.Application.Auth.Access;
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
/// An already deleted profile returns Gone without a second success audit row.
///
/// Hard-delete + Identity-DELETE + audit-anonymisering sker av HardDeleteAccountsJob
/// efter 30-dagars restore-fönster (ADR 0024 D5+D6).
///
/// Anropas från POST /me/delete-endpoint. Kräver re-autentisering (en ändamålsbunden grant,
/// #1739, ADR 0142 D5): <c>IReauthenticatingRequest</c> gör att <c>ReauthenticationBehavior</c>
/// löser in granten server-side FÖRE handlern körs (C5, epik #481) — en kapad long-lived session kan
/// alltså inte radera kontot utan koden som mejlats till kontots egen adress. Granten når aldrig
/// handlern och loggas aldrig. Endpoint ansvarar för <c>ISessionStore.MarkUserDeletedAsync</c> +
/// <c>InvalidateAllForUserAsync</c> post-commit för att avsluta alla aktiva sessioner.
/// Provider-login erasure participates in the protected lifecycle transaction before its audit and commit.
/// </summary>
public sealed record DeleteAccountCommand(string? ReauthGrant)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IReauthenticatingRequest, IAuditableCommand<Result<Guid>>,
      IAccountAccessMutation
{
    public Guid? TargetUserId => null;
    public string EventType => "Account.Deleted";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
