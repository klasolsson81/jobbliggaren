using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Auditing;

/// <summary>
/// Flat entity för GDPR Art. 5(2)-accountability. Skrivs av AuditBehavior
/// (Application-lager) inom samma transaction som den auditerade mutationen.
/// Ingen aggregate root, inga invarianter, inga domain events — write-only.
/// Schema: BUILD.md §7.1. Strategi: ADR 0022.
/// </summary>
public sealed class AuditLogEntry : Entity<AuditLogEntryId>
{
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? ImpersonatedBy { get; private set; }
    public string EventType { get; private set; } = null!;
    public string AggregateType { get; private set; } = null!;
    public Guid AggregateId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Serialized JSON-payload (jsonb i Postgres). ADR 0022 reserverade kolumnen
    /// för Fas 4 (command-audit-payload med PII-saner-krav). ADR 0035 aktiverar
    /// den för Fas 2 system-events (counts + tidsstämplar, ingen PII).
    /// Förblir null för command-audit tills Fas 4-spec landar.
    /// </summary>
    public string? Payload { get; private set; }

    // EF Core constructor
    private AuditLogEntry() { }

    private AuditLogEntry(
        AuditLogEntryId id,
        DateTimeOffset occurredAt,
        Guid correlationId,
        Guid? userId,
        Guid? impersonatedBy,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string? ipAddress,
        string? userAgent,
        string? payload) : base(id)
    {
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        UserId = userId;
        ImpersonatedBy = impersonatedBy;
        EventType = eventType;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Payload = payload;
    }

    /// <summary>
    /// Faktor för command-audit-rader (skrivs av <c>AuditBehavior</c>).
    /// </summary>
    /// <remarks>
    /// <b>#842 — <paramref name="payload"/> is no longer hard-coded to null.</b> It is opt-in per
    /// command via <c>IAuditPayloadCommand</c> and <b>must</b> already be free of un-pseudonymised
    /// personal data when it arrives here: this entity is a write-only record and does not sanitise
    /// (CLAUDE.md §5).
    /// </remarks>
    public static AuditLogEntry Create(
        DateTimeOffset occurredAt,
        Guid correlationId,
        Guid? userId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string? ipAddress,
        string? userAgent,
        Guid? impersonatedBy = null,
        string? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);

        if (aggregateId == Guid.Empty)
            throw new ArgumentException("AggregateId får inte vara tom Guid.", nameof(aggregateId));

        return new AuditLogEntry(
            AuditLogEntryId.New(),
            occurredAt,
            correlationId,
            userId,
            impersonatedBy,
            eventType,
            aggregateType,
            aggregateId,
            ipAddress,
            userAgent,
            payload);
    }

    /// <summary>
    /// Faktor för system-event-audit-rader (skrivs av
    /// <c>ISystemEventAuditor</c>-impl per ADR 0035). System har ingen
    /// request-context → userId/ip/userAgent/impersonatedBy är alltid null.
    /// AggregateId-invarianten (non-Empty Guid) bevaras — bypass-porten
    /// ansvarar för att propagera per-run-Guid (typiskt Hangfire jobId).
    /// </summary>
    public static AuditLogEntry CreateSystemEvent(
        DateTimeOffset occurredAt,
        Guid correlationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);

        if (aggregateId == Guid.Empty)
            throw new ArgumentException("AggregateId får inte vara tom Guid.", nameof(aggregateId));

        return new AuditLogEntry(
            AuditLogEntryId.New(),
            occurredAt,
            correlationId,
            userId: null,
            impersonatedBy: null,
            eventType,
            aggregateType,
            aggregateId,
            ipAddress: null,
            userAgent: null,
            payload);
    }
}
