using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Login;

// RememberMe = the "Håll mig inloggad" opt-in. true → a Persistent session (long sliding
// window + rotation); false/absent → today's reach (Legacy) until the checkbox + the
// safe-default flip (unticked → short Session) ship together in the activation PR.
public sealed record LoginCommand(
    string? Email,
    string? Password,
    bool RememberMe = false) : ICommand<Result<SessionDto>>;
