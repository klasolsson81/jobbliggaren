using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;

/// <summary>
/// #1739 — a signed-in user asks for a re-authentication code (ADR 0142 D5). It carries nothing: the
/// address is the account's own, read from Identity by the session's user id, never from the client. The
/// answer is the challenge id the code is later presented against; it is the requester's alone and leaves
/// the session in no log line, no error body and no URL, since three presentations by anyone holding it
/// burn the owner's code.
/// </summary>
public sealed record RequestReauthenticationChallengeCommand
    : ICommand<Result<ChallengeId>>, IAuthenticatedRequest;
