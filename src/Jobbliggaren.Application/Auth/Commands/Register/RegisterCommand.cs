using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Register;

// RememberMe mirrors LoginCommand — the "Håll mig inloggad" opt-in at registration. It applies only
// on the legacy instant-login path (flag OFF); email-confirmation-first registration (#714) mints no
// session, so RememberMe is inert there.
public sealed record RegisterCommand(
    string? Email,
    string? Password,
    string? DisplayName,
    bool RememberMe = false) : ICommand<Result<RegisterOutcome>>;
