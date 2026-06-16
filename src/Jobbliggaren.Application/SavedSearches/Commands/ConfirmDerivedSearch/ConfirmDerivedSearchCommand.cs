using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Commands.ConfirmDerivedSearch;

/// <summary>
/// Creates a SavedSearch from the user's CONFIRMED CV-derived occupation selection (Fas 4 STEG B,
/// ADR 0040 Beslut 4 — the deterministic engine PROPOSES, the user CONFIRMS; no SavedSearch is
/// ever created without explicit confirmation). NO AI/LLM (ADR 0071).
///
/// <para><b>Bearing invariant (DerivedSavedSearchInvariantTests):</b> this command takes the
/// user's CHOSEN ssyk-4 ids as PLAIN input (<see cref="OccupationGroup"/>) — it must NEVER
/// reference the deriver port or its result (<c>IOccupationCodeDeriver</c> /
/// <c>OccupationDerivationResult</c> / <c>OccupationCandidate</c>). The user can equally confirm
/// proposed ids or add their own (manual SSYK selection, ADR 0040 amendment). Provenance is
/// recorded via a domain event only — no DerivedFromResumeId column (ADR 0040 Beslut 3).</para>
/// </summary>
public sealed record ConfirmDerivedSearchCommand(
    string Name,
    IReadOnlyList<string> OccupationGroup,
    Guid? SourceParsedResumeId,
    IReadOnlyList<string>? Municipality,
    IReadOnlyList<string>? Region,
    IReadOnlyList<string>? EmploymentType,
    IReadOnlyList<string>? WorktimeExtent,
    string? Q,
    JobAdSortBy SortBy,
    bool NotificationEnabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "SavedSearch.CreatedFromResume";
    public string AggregateType => "SavedSearch";
    public Guid ExtractAggregateId(Result<Guid> response) =>
        response.IsSuccess ? response.Value : Guid.Empty;
}
