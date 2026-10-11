using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// One job ad for an anonymous reader (ADR 0160). Deliberately not an
/// <c>IAuthenticatedRequest</c>: it is served without a session.
/// </summary>
public sealed record GetPublicJobAdQuery(Guid Id) : IQuery<Result<PublicJobAdDto>>;
