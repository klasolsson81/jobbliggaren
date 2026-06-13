using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;

public sealed record GetMyProfileQuery : IQuery<JobSeekerProfileDto?>, IAuthenticatedRequest;
