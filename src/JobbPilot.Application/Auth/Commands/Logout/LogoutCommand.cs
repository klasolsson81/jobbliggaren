using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Logout;

public sealed record LogoutCommand : ICommand<Result>, IAuthenticatedRequest;
