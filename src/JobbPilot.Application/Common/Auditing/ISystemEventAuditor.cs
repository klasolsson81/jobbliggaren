namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Port för audit-rader från system-orchestrerade aktiviteter (Hangfire-jobb)
/// som inte passerar Mediator-pipelinen och därmed inte fångas av
/// <see cref="AuditBehavior{TMessage,TResponse}"/>. Implementeras i
/// Infrastructure-lagret och anropas endast av specifika system-jobb.
///
/// Audit-bypass-pattern parallell till <see cref="IAuditTrailEraser"/>:
/// dedikerad port, architecture-test låser konsumentlistan, ingen smyg
/// in i normala command-handlers. Per ADR 0035.
///
/// Best-effort-semantik (ADR 0035 §6): vid Hangfire-retry är insert
/// idempotent via lookup på <c>(EventType, AggregateId)</c>. Audit-rad
/// skrivs i separat <c>SaveChangesAsync</c> efter att jobbet processat
/// alla items — inte atomic med per-item-arbetet. Critical-log + CloudWatch-
/// alarm vid retry-exhaustion. GDPR Art. 30 record-of-processing
/// uppfylls — Art. 5(2) "demonstrate compliance" kräver inte
/// per-mutation-atomicitet för system-events.
/// </summary>
public interface ISystemEventAuditor
{
    /// <summary>
    /// Skriver en audit-rad för ett system-event. Idempotent vid retry
    /// (om en rad med samma <see cref="SystemAuditEvent.EventType"/> och
    /// <see cref="SystemAuditEvent.AggregateId"/> redan finns: skip).
    /// </summary>
    Task RecordAsync(SystemAuditEvent evt, CancellationToken cancellationToken);
}
