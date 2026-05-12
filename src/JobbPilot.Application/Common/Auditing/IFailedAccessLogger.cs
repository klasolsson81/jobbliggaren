namespace JobbPilot.Application.Common.Auditing;

/// <summary>
/// Strukturerad logger för failed cross-user-access-attempts (TD-67 / ADR 0031).
/// Skiljt från audit_log-tabellen (ADR 0022) — failed-access är ops-signal för
/// anomaly-detection (CloudWatch metric filter + SNS-alarm), inte compliance-
/// artefakt. Anropas av handler ENDAST när ownership-check skulle ha matchat
/// utan user-filter (dvs aggregat finns men tillhör annan user). Okänt id
/// (legitim typo) loggas INTE.
///
/// Implementeras med <c>ILogger&lt;FailedAccessLogger&gt;</c> + fasta property-
/// namn så CloudWatch metric filter kan parsa strukturerade fält. PII (ip,
/// email, payload) loggas aldrig — bara aggregate-typ, aggregat-id, requesting
/// user-id, operation.
/// </summary>
public interface IFailedAccessLogger
{
    /// <summary>
    /// Loggar att <paramref name="requestingUserId"/> försökte komma åt
    /// <paramref name="aggregateType"/> <paramref name="requestedAggregateId"/>
    /// via <paramref name="operation"/> men ownership-check misslyckades.
    /// </summary>
    /// <param name="aggregateType">Domän-aggregat-namn (samma vokabulär som
    /// <c>IAuditableCommand.AggregateType</c> per ADR 0022 — t.ex. "Application",
    /// "Resume").</param>
    /// <param name="operation">Handler-namn utan "Handler"-suffix
    /// (t.ex. "GetApplicationById", "TransitionTo").</param>
    void LogCrossUserAttempt(
        string aggregateType,
        Guid requestedAggregateId,
        Guid requestingUserId,
        string operation);
}
