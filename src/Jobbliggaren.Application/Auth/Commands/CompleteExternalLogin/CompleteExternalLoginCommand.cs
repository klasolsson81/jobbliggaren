using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

/// <summary>
/// #1744 — the provider's callback, relayed by the web after it has matched the state against its Lax cookie
/// (ADR 0142 D8). The code and the state are credentials and are never logged.
/// </summary>
public sealed record CompleteExternalLoginCommand(string? Provider, string? Code, string? State)
    : ICommand<Result<ExternalLoginCompletion>>;

/// <summary>The same outcome union as a code or a link, and the post-login path the flow carried.</summary>
public sealed record ExternalLoginCompletion(LoginOutcome Outcome, string Next);
