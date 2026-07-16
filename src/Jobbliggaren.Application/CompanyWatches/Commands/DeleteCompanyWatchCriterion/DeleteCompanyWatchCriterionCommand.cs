using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.DeleteCompanyWatchCriterion;

/// <summary>
/// DELETE /api/v1/me/company-watch-criteria/{id} — a HARD delete (C-D8 verdict, CTO Fork G1
/// 2026-07-16, the #782/ADR 0104 template). The criterion's whole payload IS the user's personal
/// data (their job-hunt predicate), no sweeper reclaims a soft-deleted row, and a deleted criterion
/// has no undo value — so the row is physically removed and Art. 5(1)(e) is satisfied by
/// construction. Deliberately NOT the soft-delete of the <c>CompanyWatch</c>/<c>SavedSearch</c>
/// siblings: their rows still mean something without their payload; this row does not.
///
/// <para>
/// Repeat delete → <c>NotFound</c> (the row is gone — the #782 precedent), unlike the idempotent
/// soft-delete siblings whose rows persist to no-op on.
/// </para>
/// </summary>
public sealed record DeleteCompanyWatchCriterionCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "CompanyWatchCriterion.Deleted";
    public string AggregateType => "CompanyWatchCriterion";
    public Guid ExtractAggregateId(Result response) => Id;
}
