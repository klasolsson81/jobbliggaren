using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.TakeLoginCode;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Takes the captured login code for an address (#1735). A command,
/// not a query: the read spends the code. UNAUTHENTICATED by design — the caller is signing in and has no
/// session. The port is registered only in Development, so outside it this command cannot resolve.
/// </summary>
public sealed record DevTakeLoginCodeCommand(string Email) : ICommand<string?>;
