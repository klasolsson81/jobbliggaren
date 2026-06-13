using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed record RegisterCommand(
    string? Email,
    string? Password,
    string? DisplayName) : ICommand<Result<SessionDto>>;
