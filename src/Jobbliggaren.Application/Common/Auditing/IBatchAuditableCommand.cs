namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Batch variant of <see cref="IAuditableCommand{TResponse}"/>: a command that
/// mutates SEVERAL aggregates of the same type in one unit of work and must
/// audit one row per aggregate (ADR 0022 parity — a per-aggregate audit query
/// must see a batch mutation exactly like N single mutations). AuditBehavior
/// writes one <c>AuditLogEntry</c> per returned id, all sharing the request's
/// correlation id (which is what groups them as one batch) and one
/// <c>OccurredAt</c> instant (one command is one moment).
///
/// A command implements EITHER this OR <see cref="IAuditableCommand{TResponse}"/>,
/// never both — AuditBehavior checks the batch marker first and would silently
/// ignore the single marker (guarded by an architecture test).
/// </summary>
public interface IBatchAuditableCommand<TResponse> : IAuditableCommand
{
    /// <summary>
    /// Returns the ids of every aggregate the command mutated, one audit row
    /// each. Called by AuditBehavior after the handler returned success. Must
    /// not contain <c>Guid.Empty</c> (rejected by <c>AuditLogEntry.Create</c>)
    /// or duplicates (one mutation = one row).
    /// </summary>
    IReadOnlyList<Guid> ExtractAggregateIds(TResponse response);
}
