using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Resumes.Queries.GetResumeById;

public sealed record GetResumeByIdQuery(Guid Id)
    : IQuery<ResumeDetailDto?>, IAuthenticatedRequest;
