using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// One page of the public catalogue (ADR 0176). Deliberately not an <c>IAuthenticatedRequest</c>: it is
/// answered for anyone. The page size
/// is a constant, not an input (<see cref="PublicJobAdCatalogueLimits.PageSize"/>).
/// </summary>
public sealed record GetPublicJobAdCatalogueQuery(int Page) : IQuery<PublicJobAdCataloguePage>;
