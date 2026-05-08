using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Resumes.Queries.GetResumes;

public sealed record GetResumesQuery(
    int PageNumber = 1,
    int PageSize = 20) : IQuery<IReadOnlyList<ResumeListItemDto>>, IAuthenticatedRequest;
