using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

/// <summary>
/// #1744 — the provider's callback (ADR 0142 D8). The code and the state are credentials and are never logged.
/// </summary>
public sealed record CompleteExternalLoginCommand(string? Provider, string? Code, string? State)
    : ICommand<Result<ExternalLoginCompletion>>;

/// <summary>
/// Where a provider login ends (ADR 0142 D8, Amendment (16)): the outcome union a code or a link shares, or, for an
/// asserted address with no link yet, the code step. Both carry the post-login path the flow carried.
/// </summary>
public abstract record ExternalLoginCompletion
{
    private ExternalLoginCompletion() { }

    /// <summary>The same outcome union as a code or a link.</summary>
    public sealed record Decided(LoginOutcome Outcome, string? Next) : ExternalLoginCompletion;

    /// <summary>
    /// A code went to the asserted address through the challenge's gates, and the pending link waits for it (#1745).
    /// The address is an echo for the code step to show; no request reads it back as proof of anything.
    /// </summary>
    public sealed record CodeRequired(ChallengeId ChallengeId, GrantToken LinkGrant, AssertedEmail Address, string? Next)
        : ExternalLoginCompletion
    {
        public const string WireName = "codeRequired";
    }
}
