using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Applications.Queries.GetApplications;

public sealed record GetApplicationsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null) : IQuery<PagedResult<ApplicationDto>>, IAuthenticatedRequest;
