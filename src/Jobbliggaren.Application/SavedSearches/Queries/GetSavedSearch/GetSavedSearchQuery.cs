using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.SavedSearches.Queries;
using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Queries.GetSavedSearch;

public sealed record GetSavedSearchQuery(Guid Id)
    : IQuery<SavedSearchDto?>, IAuthenticatedRequest;
