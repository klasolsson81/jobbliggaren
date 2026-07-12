using Jobbliggaren.Application.Dev.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.ConfirmEmail;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Delegates to the dev-only
/// <see cref="IDevEmailConfirmer"/> port (implemented in Infrastructure over
/// <c>UserManager</c>). No <c>IAppDbContext</c> / UnitOfWork involvement: the force-
/// confirm mutates the Identity store directly through the port, which persists
/// itself. Kept a thin pass-through on purpose — the risky primitive lives behind the
/// port, and the Application layer stays free of any Identity dependency.
/// </summary>
public sealed class ConfirmEmailDevCommandHandler(IDevEmailConfirmer confirmer)
    : ICommandHandler<ConfirmEmailDevCommand, DevEmailConfirmOutcome>
{
    public async ValueTask<DevEmailConfirmOutcome> Handle(
        ConfirmEmailDevCommand command, CancellationToken cancellationToken)
        => await confirmer.ForceConfirmByEmailAsync(command.Email, cancellationToken);
}
