namespace Jobbliggaren.Domain.Auditing;

public readonly record struct AuditLogEntryId(Guid Value)
{
    public static AuditLogEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
