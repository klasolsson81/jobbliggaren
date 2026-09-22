using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;

/// <summary>
/// #1739 — a signed-in user presents the re-authentication code (ADR 0142 D5). The challenge id is the one
/// <c>RequestReauthenticationChallengeCommand</c> answered. A verified code yields a
/// <c>GrantPurpose.Reauthentication</c> grant bound to the session's user, never a session; the grant is
/// what the sensitive operation then carries (<see cref="IReauthenticatingRequest"/>).
/// </summary>
public sealed record VerifyReauthenticationChallengeCommand(string? ChallengeId, string? Code)
    : ICommand<Result<GrantToken>>, IAuthenticatedRequest;
