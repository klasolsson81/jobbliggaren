using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;

namespace JobbPilot.Application.SavedSearches.Commands.CreateSavedSearch;

// ADR 0042 Beslut B — multi-värde-listor (IReadOnlyList). null = ej angivet
// (SearchCriteria.Create normaliserar → tom lista = inget filter).
// ADR 0067 Fas C2 (CTO-dom (e)/(f) 2026-06-09): Ssyk (occupation-name) UTGICK —
// ersatt av OccupationGroup (ssyk-level-4) + Municipality. En gammal klient
// som POST:ar "ssyk" får fältet tyst ignorerat (System.Text.Json default) →
// SearchCriteria.Empty-400 om inget annat kriterium (fail-säkert, ingen tyst
// halvspara).
public sealed record CreateSavedSearchCommand(
    string Name,
    IReadOnlyList<string>? OccupationGroup,
    IReadOnlyList<string>? Municipality,
    IReadOnlyList<string>? Region,
    string? Q,
    JobAdSortBy SortBy,
    bool NotificationEnabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "SavedSearch.Created";
    public string AggregateType => "SavedSearch";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
