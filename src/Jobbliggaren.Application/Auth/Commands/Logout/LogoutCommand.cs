using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Logout;

public sealed record LogoutCommand : ICommand<Result>, IAuthenticatedRequest;
