using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumes;

public sealed record GetResumesQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ResumeListItemDto>>, IAuthenticatedRequest;
