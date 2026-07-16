using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.UpdateCompanyWatchCriterion;

/// <summary>
/// PATCH /api/v1/me/company-watch-criteria/{id} (CTO Fork G6 — PATCH partial, the
/// <c>UpdateSavedSearchCommand</c> pattern-sibling). Only sent members change:
/// <see cref="Criteria"/> (non-null) → <c>UpdateCriteria</c> — the WHOLE predicate is replaced,
/// because <c>CompanyWatchCriteriaSpec</c> cannot be partially valid (both axes required, Fork B1);
/// <see cref="Label"/> (non-null) → <c>Rename</c> — a present-but-blank label CLEARS it (the
/// aggregate's <c>NormalizeLabel</c>: "no label" and "a label of spaces" are the same intent),
/// while an absent (<c>null</c>) label is untouched. A PATCH with neither member is a valid no-op.
/// </summary>
public sealed record UpdateCompanyWatchCriterionCommand(
    Guid Id,
    string? Label,
    CompanyWatchCriteriaInput? Criteria)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "CompanyWatchCriterion.Updated";
    public string AggregateType => "CompanyWatchCriterion";
    public Guid ExtractAggregateId(Result response) => Id;
}
