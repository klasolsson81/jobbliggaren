using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;

/// <summary>
/// #1739 — a signed-in user presents the code mailed to the NEW address (ADR 0142 D5). The challenge id is the one
/// <c>ChangeEmailCommand</c> answered. A verified code yields a <c>GrantPurpose.ChangeEmail</c> grant bound to the
/// session's user and the proven address, never a session; <c>ConfirmEmailChangeCommand</c> redeems it. Named so
/// the re-auth tripwire's pattern does not match: its credential is the code, not a re-authentication grant.
/// </summary>
public sealed record VerifyEmailChangeChallengeCommand(string? ChallengeId, string? Code)
    : ICommand<Result<GrantToken>>, IAuthenticatedRequest;
