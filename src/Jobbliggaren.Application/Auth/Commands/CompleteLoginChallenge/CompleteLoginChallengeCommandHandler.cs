using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;

/// <summary>
/// #1737 — the last step of registering by a proven inbox (ADR 0142 D3). Every refusal of the grant is one
/// answer, and the session is opened by the outcome function the two proof handlers share, so this handler
/// does not reach the session grant.
/// </summary>
public sealed class CompleteLoginChallengeCommandHandler(
    IOptions<AuthOptions> authOptions,
    IGrantStore grants,
    IRegistrationClaim claim,
    LoginSubjectResolver subjects,
    AccountRegistrar registrar,
    LoginProofOutcome outcome)
    : ICommandHandler<CompleteLoginChallengeCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        CompleteLoginChallengeCommand command, CancellationToken cancellationToken)
    {
        // The kill-switch, first and reading no input. Nothing below runs while it is off, so a closed host
        // never reaches the grant store or the claim.
        if (!authOptions.Value.RegistrationsOpen)
        {
            return Result.Failure<LoginOutcome>(DomainError.Validation(
                AuthErrorCodes.RegistrationsClosed, AuthErrorCodes.RegistrationsClosedMessage));
        }

        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(command.GrantToken!), GrantAssertion.Bearer(GrantPurpose.LoginComplete), cancellationToken);
        if (subject is not GrantSubject.LoginComplete { ProvenEmail: var email })
            return GrantUnusable();

        // Taken AFTER the redeem: a loser still holding a live grant could retry into the winner's account.
        // The loser is answered like a replayed grant, so nothing says an address is being registered.
        if (!await claim.TryClaimAsync(email, cancellationToken))
            return GrantUnusable();

        if (await subjects.ResolveAsync(email, cancellationToken) is LoginSubject.NoAccount)
        {
            var opened = await registrar.OpenAsync(email, cancellationToken);
            if (opened.IsFailure)
                return Result.Failure<LoginOutcome>(opened.Error);
        }

        // Whatever the address now resolves to: the account just created, one registered meanwhile, one
        // pending deletion, or a row without a profile. The Identity write may have committed, so from here
        // on CancellationToken.None.
        return Result.Success(await outcome.ResolveAsync(
            new LoginChallengeProof(email), LoginMethod.Code, CancellationToken.None));
    }

    private static Result<LoginOutcome> GrantUnusable() => Result.Failure<LoginOutcome>(
        DomainError.Gone(AuthErrorCodes.LoginGrantUnusable, AuthErrorCodes.LoginGrantUnusableMessage));
}
