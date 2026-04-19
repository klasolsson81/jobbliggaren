using JobbPilot.Application.Auth.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Commands.Register;

public sealed record RegisterCommand(
    string? Email,
    string? Password,
    string? DisplayName) : ICommand<Result<AuthTokensDto>>;
