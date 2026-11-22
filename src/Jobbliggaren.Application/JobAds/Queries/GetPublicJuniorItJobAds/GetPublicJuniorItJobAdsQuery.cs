using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// One page of the open junior IT list at <c>/junior</c> (ADR 0186), optionally in one kommun. Deliberately neither an
/// <c>IAuthenticatedRequest</c> nor an <c>IRecentSearchCandidate</c>: it is answered for anyone, and an anonymous read
/// never records a search. The page size is a constant (<see cref="PublicJuniorItJobAdLimits.PageSize"/>).
/// </summary>
public sealed record GetPublicJuniorItJobAdsQuery(string? Municipality, JobAdSortBy SortBy, int Page)
    : IQuery<PublicJuniorItJobAdsPage>;
