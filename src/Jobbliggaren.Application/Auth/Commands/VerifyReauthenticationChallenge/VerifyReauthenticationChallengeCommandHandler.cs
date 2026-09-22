using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;

/// <summary>
/// #1739 — the code arm of a re-authentication challenge (ADR 0142 D5). The store asserts the binding the
/// handler passes — this purpose, the session's user — so a login challenge, another user's challenge or
/// another purpose's is answered as missing before any code is compared. A verified code is a grant for this
/// user and nothing else: the handler reaches neither the outcome function nor the session store, which
/// <c>ReauthenticationChainTests</c> pins.
/// </summary>
public sealed class VerifyReauthenticationChallengeCommandHandler(
    ICurrentUser currentUser,
    ILoginChallengeStore store,
    IGrantStore grants)
    : ICommandHandler<VerifyReauthenticationChallengeCommand, Result<GrantToken>>
{
    public async ValueTask<Result<GrantToken>> Handle(
        VerifyReauthenticationChallengeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<GrantToken>(
                DomainError.Validation("Auth.NotAuthenticated", "Inloggning krävs för att bekräfta koden."));

        var userId = currentUser.UserId.Value;

        var verdict = await store.ConsumeBoundCodeAsync(
            ChallengeId.FromRaw(command.ChallengeId!),
            LoginCode.FromRaw(command.Code!),
            new ChallengeBinding(ChallengePurpose.Reauthentication, userId),
            cancellationToken);

        if (!verdict.IsVerified)
            return Result.Failure<GrantToken>(ChallengeVerdictErrors.For(verdict));

        return Result.Success(await grants.IssueAsync(new GrantSubject.Reauthentication(userId), cancellationToken));
    }
}
