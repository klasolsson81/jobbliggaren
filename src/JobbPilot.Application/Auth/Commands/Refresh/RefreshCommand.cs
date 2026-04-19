using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Refresh;

public sealed record RefreshCommand(string? RefreshToken) : ICommand<Result<AuthTokensDto>>;
