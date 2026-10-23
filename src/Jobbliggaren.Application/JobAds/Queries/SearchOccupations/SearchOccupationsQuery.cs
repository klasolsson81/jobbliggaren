using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.SearchOccupations;

/// <summary>
/// #2198 (ADR 0180) — the occupation pickers' free-text search, answered by
/// <see cref="IOccupationSearcher"/>. <c>q</c>-shaped and debounced on the client, the word travels
/// and structured rows come back; nothing is stored. Authenticated because the pickers are, and
/// because the route's rate-limit bucket is partitioned on the signed-in user.
/// </summary>
public sealed record SearchOccupationsQuery(string Query)
    : IQuery<OccupationSearchResult>, IAuthenticatedRequest
{
    /// <summary>Two characters: the shortest acceptance words are two letters (it, hr, UX).</summary>
    public const int MinQueryLength = 2;

    /// <summary>Every mapped occupation-name fits, so "typed in full" holds for each one.</summary>
    public const int MaxQueryLength = 100;
}
