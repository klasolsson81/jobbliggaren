using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Auditing;

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
        string? userAgent) : base(id)
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
    }

    public static AuditLogEntry Create(
        DateTimeOffset occurredAt,
        Guid correlationId,
        Guid? userId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string? ipAddress,
        string? userAgent,
        Guid? impersonatedBy = null)
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
            userAgent);
    }
}
