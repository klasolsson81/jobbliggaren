namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Port för GDPR Art. 17-anonymisering av audit-trail. Implementeras i
/// Infrastructure-lagret och anropas endast av HardDeleteAccountsJob
/// (per ADR 0024 delbeslut 3 + 6).
///
/// Audit-bypass-pattern: bryter normalt "audit_log är write-only"-disciplinen
/// (ADR 0022). Denna port är medvetet isolerad så att anonymisering är ett
/// dedikerat steg vid kontoradering, inte en yta som kan smyga in i normala
/// command-handlers. Architecture test verifierar att porten bara refereras
/// av HardDeleteAccountsJob.
///
/// Anonymiseringspolicy (ADR 0022 + ADR 0024):
///   Sätt till NULL: user_id, ip_address, user_agent
///   Bevaras 90 dagar för accountability (Art. 17(3)(b) + Art. 5(2)):
///     correlation_id, event_type, aggregate_type, aggregate_id, occurred_at
/// Anonymiserade rader är inte längre PII enligt Art. 4(1) och retention-
/// jobbet (AuditLogRetentionJob) tar bort dem efter 90 dagar.
/// </summary>
public interface IAuditTrailEraser
{
    /// <summary>
    /// Anonymiserar alla audit-rader som hör till en användare per GDPR Art. 17.
    /// Idempotent — kan köras flera gånger utan biverkningar (NULL-värden
    /// förblir NULL).
    ///
    /// Atomicitet: ExecuteSqlAsync startar ingen egen transaction. Anroparen
    /// (HardDeleteAccountsJob) ansvarar för att öppna explicit
    /// BeginTransactionAsync runt detta anrop plus efterföljande hard-delete-
    /// operationer (per ADR 0024 D3 atomicitet-kommentar).
    /// </summary>
    /// <returns>Antal rader anonymiserade. För logging.</returns>
    Task<int> AnonymizeUserAuditTrailAsync(Guid userId, CancellationToken cancellationToken);
}
