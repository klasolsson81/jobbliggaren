using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;

/// <summary>
/// #1735 — the code arm of a login challenge. It never reads a password and never touches lockout
/// (ADR 0142 D3): the store's attempt counter is the only brake on guessing, and a verified code leads to
/// the one outcome function the link arm shares.
/// </summary>
public sealed class VerifyLoginChallengeCommandHandler(ILoginChallengeStore store, LoginProofOutcome outcome)
    : ICommandHandler<VerifyLoginChallengeCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        VerifyLoginChallengeCommand command, CancellationToken cancellationToken)
    {
        var verdict = await store.ConsumeCodeAsync(
            ChallengeId.FromRaw(command.ChallengeId!), LoginCode.FromRaw(command.Code!), cancellationToken);

        if (verdict.IsVerified)
            return Result.Success(await outcome.ResolveAsync(verdict.Proof, LoginMethod.Code, cancellationToken));

        return Result.Failure<LoginOutcome>(verdict.Outcome switch
        {
            // The page warns before the burn; keyed on the count, so it stays true if MaxAttempts moves.
            ChallengeOutcome.Wrong when verdict.AttemptsRemaining == 1 => DomainError.Validation(
                AuthErrorCodes.LoginCodeWrongLastAttempt, AuthErrorCodes.LoginCodeWrongLastAttemptMessage),
            ChallengeOutcome.Wrong => DomainError.Validation(
                AuthErrorCodes.LoginCodeWrong, AuthErrorCodes.LoginCodeWrongMessage),
            ChallengeOutcome.Burned => DomainError.Gone(
                AuthErrorCodes.LoginCodeBurned, AuthErrorCodes.LoginCodeBurnedMessage),
            _ => DomainError.Gone(AuthErrorCodes.LoginCodeExpired, AuthErrorCodes.LoginCodeExpiredMessage),
        });
    }
}
