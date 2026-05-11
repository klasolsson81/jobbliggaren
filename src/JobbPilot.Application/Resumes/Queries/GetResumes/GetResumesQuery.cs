using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Resumes.Queries.GetResumes;

public sealed record GetResumesQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ResumeListItemDto>>, IAuthenticatedRequest;
