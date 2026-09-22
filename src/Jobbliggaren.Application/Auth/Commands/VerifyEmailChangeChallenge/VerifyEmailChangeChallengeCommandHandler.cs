using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;

/// <summary>
/// #1739 — the code arm of a change-email challenge (ADR 0142 D5). The store asserts the binding the handler passes —
/// the change-email purpose, the session's user — so a login challenge, a re-authentication challenge or another
/// user's is answered as missing. The grant carries the address the record was written for, which the request step
/// took from its validated command.
/// </summary>
public sealed class VerifyEmailChangeChallengeCommandHandler(
    ICurrentUser currentUser,
    ILoginChallengeStore store,
    IGrantStore grants)
    : ICommandHandler<VerifyEmailChangeChallengeCommand, Result<GrantToken>>
{
    public async ValueTask<Result<GrantToken>> Handle(
        VerifyEmailChangeChallengeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<GrantToken>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att bekräfta koden."));

        var userId = currentUser.UserId.Value;

        var verdict = await store.ConsumeBoundCodeAsync(
            ChallengeId.FromRaw(command.ChallengeId!),
            LoginCode.FromRaw(command.Code!),
            new ChallengeBinding(ChallengePurpose.ChangeEmail, userId),
            cancellationToken);

        if (!verdict.IsVerified)
            return Result.Failure<GrantToken>(ChallengeVerdictErrors.For(verdict));

        return Result.Success(await grants.IssueAsync(
            new GrantSubject.ChangeEmail(userId, verdict.Proof.ProvenEmail), cancellationToken));
    }
}
