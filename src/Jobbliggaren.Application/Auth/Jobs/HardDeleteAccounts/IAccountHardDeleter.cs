namespace Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

/// <summary>
/// Port för konto-hard-deletion-operationer per ADR 0024 D6. Implementeras i
/// Infrastructure-lagret eftersom den korsar AppDbContext + AppIdentityDbContext
/// (cross-context-DDL + UserManager.DeleteAsync). Anropas endast av
/// HardDeleteAccountsJob — architecture test verifierar isolering.
///
/// Operationerna är split:ade i fyra metoder för att hålla orchestratorn
/// (HardDeleteAccountsJob) i Application-lagret med tunn ansvarsyta:
/// loop + cancel-token-management + progress-log. All cross-context-mekanik
/// + transaktioner sker bakom porten.
/// </summary>
public interface IAccountHardDeleter
{
    /// <summary>
    /// Cleans historical Identity-only orphans after the grace period.
    /// Current registration and hard deletion share an atomic lifecycle transaction.
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
    /// Erases one eligible account's owned graph and DEK, anonymizes its audit,
    /// and deletes Identity in one protected physical transaction.
    /// Failure retains the whole account for a later attempt.
    /// </summary>
    Task HardDeleteAccountAsync(Guid jobSeekerId, CancellationToken cancellationToken);

    /// <summary>
    /// Steg 3 — the backstop for the account-deletion endpoint's erasure of external logins (ADR 0142 Amendment
    /// (20)): every login still held by a soft-deleted account, whichever provider wrote it.
    /// </summary>
    /// <returns>How many login rows it deleted.</returns>
    Task<int> EraseExternalLoginsOfAccountsPendingDeletionAsync(CancellationToken cancellationToken);
}
