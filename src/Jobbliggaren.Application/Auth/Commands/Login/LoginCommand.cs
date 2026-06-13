using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Login;

public sealed record LoginCommand(
    string? Email,
    string? Password) : ICommand<Result<SessionDto>>;
