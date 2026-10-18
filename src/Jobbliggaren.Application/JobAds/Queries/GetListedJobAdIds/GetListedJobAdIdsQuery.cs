using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

/// <summary>
/// The ids of one job-sitemap part (ADR 0174). Deliberately not an <c>IAuthenticatedRequest</c>: it is answered for
/// anyone, from the listed-ad snapshot, with ids and nothing else.
/// </summary>
public sealed record GetListedJobAdIdsQuery(int Part, int Parts) : IQuery<ListedJobAdPart>;
