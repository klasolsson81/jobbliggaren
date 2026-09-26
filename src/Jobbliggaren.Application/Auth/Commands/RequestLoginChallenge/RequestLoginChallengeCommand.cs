using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;

/// <summary>
/// #1735 — request a login challenge for an address (ADR 0142 D2). Answers the challenge's id for every
/// well-formed address; the mail, and which mail, is decided off the request path.
/// </summary>
public sealed record RequestLoginChallengeCommand(string? Email) : ICommand<Result<ChallengeId>>;
