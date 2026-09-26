using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Dev.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.SeedAccount;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Opens an account through <see cref="AccountRegistrar"/>, the writer
/// <c>complete</c> uses, so a seeded account is the state a registration produces. An existing account is never
/// changed. <see cref="DevSeedAccountOutcome.Ready"/> means what the suite needs: the login resolves the address to
/// <see cref="LoginSubject.Active"/>, and so mints a code for it. A profile pending deletion or a missing one would
/// get no code, so they answer <see cref="DevSeedAccountOutcome.Unavailable"/>.
/// </summary>
public sealed class DevSeedAccountCommandHandler(
    IDevSeedableAddressPolicy policy,
    LoginSubjectResolver subjects,
    AccountRegistrar registrar)
    : ICommandHandler<DevSeedAccountCommand, DevSeedAccountOutcome>
{
    public async ValueTask<DevSeedAccountOutcome> Handle(
        DevSeedAccountCommand command, CancellationToken cancellationToken)
    {
        if (!policy.IsSeedable(command.Email))
            return DevSeedAccountOutcome.NotReserved;

        var subject = await subjects.ResolveAsync(command.Email, cancellationToken);
        if (subject is LoginSubject.NoAccount)
        {
            await registrar.OpenAsync(command.Email, cancellationToken);
            subject = await subjects.ResolveAsync(command.Email, cancellationToken);
        }

        return subject is LoginSubject.Active ? DevSeedAccountOutcome.Ready : DevSeedAccountOutcome.Unavailable;
    }
}
