using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.CreateCompanyWatchCriterion;

/// <summary>
/// #560 PR-3 (CTO Fork G6) — create a criteria-based company watch for the current user. Returns
/// the new <c>CompanyWatchCriterionId</c>. NOT idempotent by content, deliberately: a duplicate
/// criterion is a cosmetic, user-deletable nuisance (PR-1's documented stance — no unique
/// constraint exists, a btree tuple over a 1000-leaf predicate would exceed the index row cap),
/// while content-based dedup here would be an invariant the storage does not back.
///
/// <para>
/// <c>IAuditableCommand</c> per DPIA C-D9 — the marker is opt-in; without it the audit log is
/// silently empty for this whole write surface. The audit row carries the aggregate id only,
/// never the predicate (counts-only discipline, C-D5).
/// </para>
/// </summary>
public sealed record CreateCompanyWatchCriterionCommand(
    CompanyWatchCriteriaInput Criteria,
    string? Label)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "CompanyWatchCriterion.Created";
    public string AggregateType => "CompanyWatchCriterion";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
