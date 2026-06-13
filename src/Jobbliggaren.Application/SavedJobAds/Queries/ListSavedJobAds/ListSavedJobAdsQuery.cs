using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;

public sealed record ListSavedJobAdsQuery
    : IQuery<IReadOnlyList<SavedJobAdDto>>, IAuthenticatedRequest;
