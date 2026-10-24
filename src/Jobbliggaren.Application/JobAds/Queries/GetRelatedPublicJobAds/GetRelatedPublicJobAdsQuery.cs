using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>
/// The related listed ads under one public job page (ADR 0182). Deliberately neither an <c>IAuthenticatedRequest</c>
/// nor an <c>ICapturesRecentSearch</c>. Its one member is the anchor's id: every selection key is read from the anchor's
/// row on the server, so no caller can choose a group, a place or an order.
/// </summary>
public sealed record GetRelatedPublicJobAdsQuery(Guid Id) : IQuery<Result<RelatedPublicJobAdsDto>>;
