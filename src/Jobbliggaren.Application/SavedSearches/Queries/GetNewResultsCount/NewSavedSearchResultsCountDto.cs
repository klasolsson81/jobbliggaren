namespace Jobbliggaren.Application.SavedSearches.Queries.GetNewResultsCount;

/// <summary>
/// #312 (ADR 0115) — per saved search: how many ACTIVE ads have been ingested since the user last
/// saw this search's results (<c>JobAd.CreatedAt &gt; SavedSearch.ResultsSeenAt</c>). The in-app
/// "N nya träffar"-badge datum. <see cref="NewCount"/> is a LIVE windowed count (no persisted hit
/// rows) — immune to the #864 archived-ad miscount and to criteria-mutation orphaning by
/// construction.
/// </summary>
public sealed record NewSavedSearchResultsCountDto(
    Guid SavedSearchId,
    string Name,
    int NewCount);
