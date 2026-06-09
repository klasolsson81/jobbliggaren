using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Application.SavedSearches.Commands.UpdateSavedSearch;

/// <summary>
/// PATCH /api/v1/saved-searches/{id}. Partiell uppdatering: endast medskickade
/// fält ändras. <see cref="Name"/> → Rename, <see cref="NotificationEnabled"/>
/// → SetNotification (ADR 0039 Beslut 4 — lagras, ingen dispatch i Fas 2),
/// <see cref="Criteria"/> (icke-null) → UpdateCriteria (hela kriteriet ersätts;
/// SearchCriteria-VO:t kan inte vara partiellt giltigt).
/// </summary>
public sealed record UpdateSavedSearchCommand(
    Guid Id,
    string? Name,
    bool? NotificationEnabled,
    SavedSearchCriteriaInput? Criteria)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "SavedSearch.Updated";
    public string AggregateType => "SavedSearch";
    public Guid ExtractAggregateId(Result response) => Id;
}

// ADR 0042 Beslut B — multi-värde-listor (IReadOnlyList).
// ADR 0067 Fas C2 (CTO-dom (e)/(f)): Ssyk UTGICK — OccupationGroup +
// Municipality ersätter (kanonisk dimensionsordning, architect F1).
public sealed record SavedSearchCriteriaInput(
    IReadOnlyList<string>? OccupationGroup,
    IReadOnlyList<string>? Municipality,
    IReadOnlyList<string>? Region,
    string? Q,
    JobAdSortBy SortBy);
