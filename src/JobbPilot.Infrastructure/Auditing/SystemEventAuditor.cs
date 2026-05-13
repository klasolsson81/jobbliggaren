using System.Text.Json;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// Implementation av <see cref="ISystemEventAuditor"/> (ADR 0035). Skriver
/// audit-rad för system-orchestrerade aktiviteter (Hangfire-jobb) som inte
/// passerar Mediator-pipelinen. Bypass-port arch-låst via
/// <c>ISystemEventAuditor_should_only_be_referenced_by_system_jobs_and_redact_handler</c>.
///
/// <para>
/// <b>Idempotens vid Hangfire-retry:</b> innan insert kontrolleras om en rad
/// med samma <c>(EventType, AggregateId)</c> redan finns. AggregateId är
/// per-run-Guid (typiskt Hangfire jobId) — duplicate-skydd vid retry-loop.
/// </para>
///
/// <para>
/// <b>Best-effort vid audit-failure (ADR 0035 §6):</b> exception bubblar till
/// Hangfire som kör automatic retry. Critical-log emit:as här för
/// CloudWatch-alarm vid retry-exhaustion.
/// </para>
/// </summary>
public sealed partial class SystemEventAuditor(
    IAppDbContext db,
    ICorrelationIdProvider correlationIdProvider,
    ILogger<SystemEventAuditor> logger) : ISystemEventAuditor
{
    public async Task RecordAsync(SystemAuditEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            // Idempotens-skydd: vid Hangfire-retry får vi inte skriva dubbla
            // audit-rader. Lookup på (EventType, AggregateId) är cheap eftersom
            // partitioneringen klipper sökytan till relevant occurred_at-fönster
            // i praktiken (audit_log är partitionerad per dag, ADR 0024 D2).
            var alreadyRecorded = await db.AuditLogEntries
                .AnyAsync(
                    a => a.EventType == evt.EventType
                         && a.AggregateId == evt.AggregateId,
                    cancellationToken);

            if (alreadyRecorded)
            {
                LogIdempotentSkip(logger, evt.EventType, evt.AggregateId);
                return;
            }

            var payload = JsonSerializer.Serialize(evt, evt.GetType());

            var entry = AuditLogEntry.CreateSystemEvent(
                occurredAt: evt.OccurredAt,
                correlationId: correlationIdProvider.Current,
                eventType: evt.EventType,
                aggregateType: evt.AggregateType,
                aggregateId: evt.AggregateId,
                payload: payload);

            db.AuditLogEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Critical-log → CloudWatch alarm vid retry-exhaustion. Exception
            // bubblar så Hangfire fångar och retry:ar (per ADR 0035 §6).
            LogAuditFailure(logger, ex, evt.EventType, evt.AggregateId);
            throw;
        }
    }

    [LoggerMessage(EventId = 5601, Level = LogLevel.Debug,
        Message = "SystemEventAuditor: idempotent skip — audit-rad redan finns för EventType={EventType}, AggregateId={AggregateId}.")]
    private static partial void LogIdempotentSkip(ILogger logger, string eventType, Guid aggregateId);

    [LoggerMessage(EventId = 5602, Level = LogLevel.Critical,
        Message = "SystemEventAuditor: kunde inte skriva audit-rad för EventType={EventType}, AggregateId={AggregateId}. GDPR Art. 30 record-of-processing kan vara påverkat.")]
    private static partial void LogAuditFailure(ILogger logger, Exception exception, string eventType, Guid aggregateId);
}
