using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Login;

public sealed record LoginCommand(
    string? Email,
    string? Password) : ICommand<Result<AuthTokensDto>>;
