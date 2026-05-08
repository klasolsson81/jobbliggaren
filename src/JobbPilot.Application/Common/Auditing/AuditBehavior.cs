using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Common.Auditing;

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
        if (message is not IAuditableCommand<TResponse> auditable)
            return response;

        // Skip audit på Result.Failure — Fas 1 auditerar bara success per ADR 0022.
        // Failed-attempts-audit retro-fittas i Fas 6 (impersonation/admin-actions).
        if (response is Result result && result.IsFailure)
            return response;

        var aggregateId = auditable.ExtractAggregateId(response);

        // TODO(Fas 6): impersonatedBy fylls när admin-impersonation införs.
        // Kräver ICurrentImpersonationContext-port. Tills dess: alltid null.
        var entry = AuditLogEntry.Create(
            occurredAt: clock.UtcNow,
            correlationId: correlationIdProvider.Current,
            userId: currentUser.UserId,
            eventType: auditable.EventType,
            aggregateType: auditable.AggregateType,
            aggregateId: aggregateId,
            ipAddress: requestContextProvider.IpAddress,
            userAgent: requestContextProvider.UserAgent);

        db.AuditLogEntries.Add(entry);

        // SaveChanges sker i UnitOfWorkBehavior:s post-action — audit-raden och
        // handler-mutationen persisteras atomiskt i samma transaction.
        return response;
    }
}
