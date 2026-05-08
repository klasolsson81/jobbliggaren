using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.MarkGhosted;

/// <summary>
/// System-jobb-command — anropas från GhostedDetectionJob (Hangfire) i Fas 3
/// när en ansökan inte fått svar inom <c>ghosted_threshold_days</c>. Saknar
/// medvetet <c>IAuthenticatedRequest</c> eftersom commandot kör utan
/// inloggad användare. Får INTE exponeras via API-endpoint utan att först
/// lägga till explicit RBAC-policy — ICurrentUser kommer vara unauthenticated.
/// Audit-rad skrivs med <c>user_id = NULL</c> (per ADR 0022).
/// </summary>
public sealed record MarkGhostedCommand(Guid ApplicationId)
    : ICommand<Result>, IAuditableCommand<Result>
{
    public string EventType => "Application.MarkedGhosted";
    public string AggregateType => "Application";
    public Guid ExtractAggregateId(Result response) => ApplicationId;
}
