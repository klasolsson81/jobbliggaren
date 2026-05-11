using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Admin.Queries.GetAuditLogEntries;

/// <summary>
/// Admin-only läs-query mot <c>audit_log</c>. Stöd för datum-range, user-id,
/// event-type och aggregate-type-filter samt paginering. Defense-in-depth:
/// <see cref="IAdminRequest"/>-markören gör att <see cref="Behaviors.AdminAuthorizationBehavior{TMessage,TResponse}"/>
/// kastar <see cref="Exceptions.ForbiddenException"/> om en icke-Admin når
/// Mediator-pipen (HTTP-lagret nekar redan via <c>RequireAuthorization("Admin")</c>).
/// </summary>
public sealed record GetAuditLogEntriesQuery(
    int Page = 1,
    int PageSize = 50,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? UserId = null,
    string? EventType = null,
    string? AggregateType = null)
    : IQuery<PagedResult<AuditLogEntryDto>>, IAdminRequest;
