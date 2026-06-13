using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<CurrentUserDto?>, IAuthenticatedRequest;
