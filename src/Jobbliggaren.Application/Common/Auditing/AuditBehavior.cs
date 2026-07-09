using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Pipeline-behavior som skriver audit-rad för commands som implementerar
/// <see cref="IAuditableCommand{TResponse}"/>. Per ADR 0022 placeras denna
/// innerst i pipelinen (registreras efter UnitOfWorkBehavior). Post-action
/// lägger till AuditLogEntry i DbContext, varefter UnitOfWorkBehavior:s
/// SaveChanges persisterar handler-mutation och audit-rad atomiskt.
/// </summary>
public sealed class AuditBehavior<TMessage, TResponse>(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationIdProvider,
    IRequestContextProvider requestContextProvider)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(message, cancellationToken);

        // Bara markerade commands triggar audit (opt-in via marker-interface).
        if (message is not (IAuditableCommand<TResponse> or IBatchAuditableCommand<TResponse>))
            return response;

        // Skip audit på Result.Failure — Fas 1 auditerar bara success per ADR 0022.
        // Failed-attempts-audit retro-fittas i Fas 6 (impersonation/admin-actions).
        if (response is Result result && result.IsFailure)
            return response;

        // Batch marker (#630 PR 9): one row per mutated aggregate, sharing one
        // OccurredAt instant (one command is one moment) and the request's
        // correlation id, which is what groups the rows as a batch. Checked
        // BEFORE the single marker — a command implements exactly one of the
        // two (architecture-test-guarded).
        if (message is IBatchAuditableCommand<TResponse> batchAuditable)
        {
            var batchOccurredAt = clock.UtcNow;
            foreach (var id in batchAuditable.ExtractAggregateIds(response))
                db.AuditLogEntries.Add(CreateEntry(batchAuditable, id, batchOccurredAt));
            return response;
        }

        var auditable = (IAuditableCommand<TResponse>)message;
        var aggregateId = auditable.ExtractAggregateId(response);
        db.AuditLogEntries.Add(CreateEntry(auditable, aggregateId, clock.UtcNow));

        // SaveChanges sker i UnitOfWorkBehavior:s post-action — audit-raden och
        // handler-mutationen persisteras atomiskt i samma transaction.
        return response;
    }

    // TODO(Fas 6): impersonatedBy fylls när admin-impersonation införs.
    // Kräver ICurrentImpersonationContext-port. Tills dess: alltid null.
    private AuditLogEntry CreateEntry(
        IAuditableCommand auditable, Guid aggregateId, DateTimeOffset occurredAt) =>
        AuditLogEntry.Create(
            occurredAt: occurredAt,
            correlationId: correlationIdProvider.Current,
            userId: currentUser.UserId,
            eventType: auditable.EventType,
            aggregateType: auditable.AggregateType,
            aggregateId: aggregateId,
            ipAddress: requestContextProvider.IpAddress,
            userAgent: requestContextProvider.UserAgent);
}
