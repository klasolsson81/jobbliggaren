namespace Jobbliggaren.Application.Admin.Queries.GetAuditLogEntries;

/// <summary>
/// Flat read-model för admin-granskning. Speglar <c>audit_log</c>-tabellen
/// fält-för-fält. Inga relationer expand:as — admin-vyn visar IDs och
/// utomstående lookup-flöde sker via separata queries i Fas 6.
/// </summary>
public sealed record AuditLogEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    Guid? UserId,
    Guid? ImpersonatedBy,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    string? IpAddress,
    string? UserAgent);
