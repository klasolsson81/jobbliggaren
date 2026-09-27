using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;

/// <summary>
/// #1735 — the code arm of a login challenge. It never reads a password and never touches lockout
/// (ADR 0142 D3): the store's attempt counter is the only brake on guessing, and a verified code leads to
/// the one outcome function the link arm shares. A pending-link grant (#1745) is redeemed only after the code
/// verified, so a wrong attempt leaves it untouched; a missing or spent one leaves the code's own outcome.
/// </summary>
public sealed class VerifyLoginChallengeCommandHandler(
    ILoginChallengeStore store, IGrantStore grants, LoginProofOutcome outcome)
    : ICommandHandler<VerifyLoginChallengeCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        VerifyLoginChallengeCommand command, CancellationToken cancellationToken)
    {
        var verdict = await store.ConsumeCodeAsync(
            ChallengeId.FromRaw(command.ChallengeId!), LoginCode.FromRaw(command.Code!), cancellationToken);

        if (!verdict.IsVerified)
            return Result.Failure<LoginOutcome>(ChallengeVerdictErrors.For(verdict));

        if (command.LinkGrant is { } linkGrant
            && await grants.RedeemAsync(
                GrantToken.FromRaw(linkGrant),
                GrantAssertion.Bearer(GrantPurpose.PendingExternalLink),
                cancellationToken) is GrantSubject.PendingExternalLink pending)
        {
            return Result.Success(
                await outcome.ResolveCodeBoundLinkAsync(verdict.Proof, pending, cancellationToken));
        }

        return Result.Success(await outcome.ResolveAsync(verdict.Proof, LoginMethod.Code, cancellationToken));
    }
}
