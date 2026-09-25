using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.SeedAccount;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Gives the Playwright E2E suite a login-capable account for a reserved
/// address without spending the login challenge's cap on mails to addresses without an account (ADR 0142 part
/// 5a; it replaces <c>/auth/register</c> + <c>/dev/confirm-email</c> as the suite's seeding path). It hands out
/// no credential: the suite still logs in through the challenge and <c>/dev/login-code</c>. Unauthenticated,
/// like the other Development seams.
/// </summary>
public sealed record DevSeedAccountCommand(string Email) : ICommand<DevSeedAccountOutcome>;

/// <summary>Outcome of <see cref="DevSeedAccountCommand"/>.</summary>
public enum DevSeedAccountOutcome
{
    /// <summary>The address resolves to an account a login can be given a session for.</summary>
    Ready,

    /// <summary>The address is not at a reserved domain; nothing was read or written.</summary>
    NotReserved,

    /// <summary>A login cannot sign in to the address. Nothing was changed.</summary>
    Unavailable,
}
