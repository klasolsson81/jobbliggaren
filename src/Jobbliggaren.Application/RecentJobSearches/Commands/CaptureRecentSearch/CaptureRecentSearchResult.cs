namespace Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;

/// <summary>
/// #2285 — whether the search is in the user's recent searches after <see cref="CaptureRecentSearchCommand"/>: false when
/// the gate refused it, and when it could not be stored.
/// </summary>
public sealed record CaptureRecentSearchResult(bool Stored);
