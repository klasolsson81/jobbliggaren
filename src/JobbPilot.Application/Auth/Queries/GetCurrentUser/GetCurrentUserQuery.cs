using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Auth.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserDto?>, IAuthenticatedRequest;
