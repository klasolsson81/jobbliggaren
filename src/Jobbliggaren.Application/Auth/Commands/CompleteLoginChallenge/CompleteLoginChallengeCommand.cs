using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;

/// <summary>
/// #1737 — a proven new address accepts the terms and gets its account (ADR 0142 D3). The command carries
/// no address: the account is created on the one the grant hands back, whatever the browser believes.
/// <see cref="AcceptTerms"/> has no default, so a body that omits it binds false and is refused.
/// </summary>
public sealed record CompleteLoginChallengeCommand(string? GrantToken, bool AcceptTerms)
    : ICommand<Result<LoginOutcome>>;
