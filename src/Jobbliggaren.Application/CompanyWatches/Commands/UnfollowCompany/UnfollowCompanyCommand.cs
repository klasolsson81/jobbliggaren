using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.UnfollowCompany;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — unfollow a company watch by its surrogate <c>CompanyWatchId</c>
/// (FORK D2 — keeps the raw org.nr out of the DELETE URL/access-log per D8(c)). Soft-delete,
/// idempotent. Owner-scoped (a watch belonging to another user yields NotFound + a cross-user
/// access-log entry, ADR 0031).
/// </summary>
public sealed record UnfollowCompanyCommand(Guid CompanyWatchId)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "CompanyWatch.Unfollowed";
    public string AggregateType => "CompanyWatch";
    public Guid ExtractAggregateId(Result response) => CompanyWatchId;
}
