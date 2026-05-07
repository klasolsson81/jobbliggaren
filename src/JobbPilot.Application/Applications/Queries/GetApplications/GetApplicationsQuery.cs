using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Applications.Queries.GetApplications;

public sealed record GetApplicationsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null) : IQuery<IReadOnlyList<ApplicationDto>>, IAuthenticatedRequest;
