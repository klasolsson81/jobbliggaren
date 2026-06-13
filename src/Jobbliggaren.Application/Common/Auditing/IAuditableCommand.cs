namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Marker-interface för commands som ska generera audit-rad. Per ADR 0022
/// triggar AuditBehavior endast på commands som implementerar
/// <see cref="IAuditableCommand{TResponse}"/>. Värden från detta interface
/// skrivs till audit_log-tabellen (BUILD.md §7.1).
/// </summary>
public interface IAuditableCommand
{
    /// <summary>
    /// Stabilt event-namn skrivet till audit_log.event_type. Format:
    /// "<AggregateType>.<Action>" (t.ex. "Application.Created",
    /// "Resume.MasterContentUpdated"). Får inte ändras retroaktivt — audit
    /// queries beror på stabila värden.
    /// </summary>
    string EventType { get; }

    /// <summary>
    /// Aggregate-typ som muteras. Skrivs till audit_log.aggregate_type
    /// (t.ex. "Application", "Resume").
    /// </summary>
    string AggregateType { get; }
}

/// <summary>
/// Generic-marker som låter AuditBehavior extrahera aggregate-ID från
/// command response (för Create-fall där ID genereras i handler) eller
/// från command-fältet (för mutation av befintliga aggregat).
/// </summary>
public interface IAuditableCommand<TResponse> : IAuditableCommand
{
    /// <summary>
    /// Returnerar aggregate-ID för audit-raden. Anropas av AuditBehavior
    /// efter att handler kört och returnerat success.
    /// </summary>
    Guid ExtractAggregateId(TResponse response);
}
