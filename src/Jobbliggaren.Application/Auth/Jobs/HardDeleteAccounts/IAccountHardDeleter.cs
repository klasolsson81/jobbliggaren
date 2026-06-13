namespace Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

/// <summary>
/// Port för konto-hard-deletion-operationer per ADR 0024 D6. Implementeras i
/// Infrastructure-lagret eftersom den korsar AppDbContext + AppIdentityDbContext
/// (cross-context-DDL + UserManager.DeleteAsync). Anropas endast av
/// HardDeleteAccountsJob — architecture test verifierar isolering.
///
/// Operationerna är split:ade i tre metoder för att hålla orchestratorn
/// (HardDeleteAccountsJob) i Application-lagret med tunn ansvarsyta:
/// loop + cancel-token-management + progress-log. All cross-context-mekanik
/// + transaktioner sker bakom porten.
/// </summary>
public interface IAccountHardDeleter
{
    /// <summary>
    /// Steg 0 — Orphan-cleanup. Hittar ApplicationUsers utan matchande JobSeeker
    /// (varken aktiv eller soft-deletad — Identity-rader som hängde kvar från
    /// tidigare körning där Steg 2 h failade). För varje orphan: UserManager.DeleteAsync.
    /// Idempotent — om Identity redan tog bort raden mellan SELECT och DELETE
    /// är det inget fel.
    /// </summary>
    /// <returns>Antal Identity-rader som rensades.</returns>
    Task<int> CleanupIdentityOrphansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Steg 1 — Hämta soft-deletade konton mogna för hard-delete.
    /// JobSeeker.deleted_at &lt; cutoff (typiskt UTC.Now - 30 days).
    /// </summary>
    /// <returns>Lista av JobSeeker.Id som ska hard-deletas.</returns>
    Task<IReadOnlyList<Guid>> GetAccountsReadyForHardDeleteAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken);

    /// <summary>
    /// Steg 2 — Hard-delete enskilt konto. Anonymisering av audit-trail +
    /// hard-delete av alla user-ägda aggregat (FK CASCADE tar barnen) sker
    /// inom explicit transaction. Identity-DELETE körs efter transactionen
    /// committats — om den failer plockas Identity-raden upp av nästa
    /// CleanupIdentityOrphansAsync-körning (idempotent fail-recovery).
    /// </summary>
    Task HardDeleteAccountAsync(Guid jobSeekerId, CancellationToken cancellationToken);
}
