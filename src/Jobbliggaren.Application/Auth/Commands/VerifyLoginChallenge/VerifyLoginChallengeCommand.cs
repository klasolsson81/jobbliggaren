using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;

/// <summary>
/// #1735 — present the code a login challenge mailed (ADR 0142 D3). The challenge id is the one
/// <c>POST /auth/challenge</c> answered, so the caller is the requester; wrong, burned and expired are told
/// apart for them.
/// </summary>
public sealed record VerifyLoginChallengeCommand(string? ChallengeId, string? Code)
    : ICommand<Result<LoginOutcome>>;
