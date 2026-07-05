using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Register;

// RememberMe mirrors LoginCommand — the "Håll mig inloggad" opt-in at registration.
public sealed record RegisterCommand(
    string? Email,
    string? Password,
    string? DisplayName,
    bool RememberMe = false) : ICommand<Result<SessionDto>>;
