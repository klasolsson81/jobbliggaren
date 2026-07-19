using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Queries.GetNewResultsCount;

/// <summary>
/// #312 (ADR 0115) — the caller's per-saved-search "N nya träffar"-counts (notification-enabled
/// searches only). In-app-only v1, GDPR Art. 6(1)(b). Owner-scoped, read-only.
/// </summary>
public sealed record GetNewSavedSearchResultsCountQuery
    : IQuery<IReadOnlyList<NewSavedSearchResultsCountDto>>;
