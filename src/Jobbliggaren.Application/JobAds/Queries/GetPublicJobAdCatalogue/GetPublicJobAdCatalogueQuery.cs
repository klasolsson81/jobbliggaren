using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// One page of the public catalogue (ADR 0175). Deliberately neither an <c>IAuthenticatedRequest</c> nor an
/// <c>ICapturesRecentSearch</c>: it is answered for anyone, and an anonymous read never records a search. The page size
/// is a constant, not an input (<see cref="PublicJobAdCatalogueLimits.PageSize"/>).
/// </summary>
public sealed record GetPublicJobAdCatalogueQuery(int Page) : IQuery<PublicJobAdCataloguePage>;
