using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Login;

// RememberMe = the "Håll mig inloggad" opt-in (#481). true → a Persistent session (long
// sliding window + id rotation); false/absent → a short session-scoped Session (the safe
// default). Both branches are live as of the 2b-3b activation (checkbox + refresh driver).
public sealed record LoginCommand(
    string? Email,
    string? Password,
    bool RememberMe = false) : ICommand<Result<SessionDto>>;
