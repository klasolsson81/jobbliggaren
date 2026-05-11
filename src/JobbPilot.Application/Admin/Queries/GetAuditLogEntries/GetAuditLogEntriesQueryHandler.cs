using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Admin.Queries.GetAuditLogEntries;

/// <summary>
/// Läser audit-log-entries paginerat. Total count i separat query per CLAUDE.md §3.6.
/// Resultat sorteras desc på OccurredAt (senaste först, index `ix_audit_log_occurred_at`
/// stödjer ordningen). Inga FK-joins — audit-table har inga FKs per ADR 0022.
/// </summary>
public sealed class GetAuditLogEntriesQueryHandler(IAppDbContext db)
    : IQueryHandler<GetAuditLogEntriesQuery, PagedResult<AuditLogEntryDto>>
{
    public async ValueTask<PagedResult<AuditLogEntryDto>> Handle(
        GetAuditLogEntriesQuery query, CancellationToken cancellationToken)
    {
        var q = db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (query.From is { } from)
            q = q.Where(a => a.OccurredAt >= from);

        if (query.To is { } to)
            q = q.Where(a => a.OccurredAt <= to);

        if (query.UserId is { } userId)
            q = q.Where(a => a.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType;
            q = q.Where(a => a.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(query.AggregateType))
        {
            var aggregateType = query.AggregateType;
            q = q.Where(a => a.AggregateType == aggregateType);
        }

        var totalCount = await q.CountAsync(cancellationToken);

        var entries = await q
            .OrderByDescending(a => a.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new AuditLogEntryDto(
                a.Id.Value,
                a.OccurredAt,
                a.CorrelationId,
                a.UserId,
                a.ImpersonatedBy,
                a.EventType,
                a.AggregateType,
                a.AggregateId,
                a.IpAddress,
                a.UserAgent))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntryDto>(entries, totalCount, query.Page, query.PageSize);
    }
}
