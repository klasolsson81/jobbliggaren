using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Applications.Queries.GetApplicationById;

public sealed record GetApplicationByIdQuery(Guid Id)
    : IQuery<ApplicationDetailDto?>, IAuthenticatedRequest;
