using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;

/// <summary>
/// #1737 — the last step of registering by a proven inbox (ADR 0142 D3), and since #1744 by a provider's proof
/// (D8). Every refusal of the grant is one answer, and the session is opened by the outcome function the proof
/// handlers share, so this handler does not reach the session grant.
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

        // One redemption for either registration grant: the store takes the token once, whichever it is.
        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(command.GrantToken!),
            GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal),
            cancellationToken);

        return subject switch
        {
            GrantSubject.LoginComplete { ProvenEmail: var email } => await CompleteByCodeAsync(email, cancellationToken),
            GrantSubject.LoginCompleteExternal external => await CompleteByProviderAsync(external, cancellationToken),
            _ => GrantUnusable(),
        };
    }

    private async Task<Result<LoginOutcome>> CompleteByCodeAsync(string email, CancellationToken ct)
    {
        // Taken AFTER the redeem: a loser still holding a live grant could retry into the winner's account.
        // The loser is answered like a replayed grant, so nothing says an address is being registered.
        if (!await claim.TryClaimAsync(email, ct))
            return GrantUnusable();

        if (await subjects.ResolveAsync(email, ct) is LoginSubject.NoAccount)
        {
            var opened = await registrar.OpenAsync(email, ct);
            if (opened.IsFailure)
                return Result.Failure<LoginOutcome>(opened.Error);
        }

        // Whatever the address now resolves to: the account just created, one registered meanwhile, one
        // pending deletion, or a row without a profile. The Identity write may have committed, so from here
        // on CancellationToken.None.
        return Result.Success(await outcome.ResolveAsync(
            new LoginChallengeProof(email), LoginMethod.Code, CancellationToken.None));
    }

    private async Task<Result<LoginOutcome>> CompleteByProviderAsync(
        GrantSubject.LoginCompleteExternal external, CancellationToken ct)
    {
        // The grant was issued from a VerifiedEmail and carries its address unchanged.
        if (VerifiedEmail.TryCreate(external.ProvenEmail) is not { } email)
            return GrantUnusable();

        if (!await claim.TryClaimAsync(external.ProvenEmail, ct))
            return GrantUnusable();

        var proof = new ExternalLoginProof(email, external.Provider, external.Subject);

        // Checked before an account is opened, so the ordinary case never creates an account it then cannot link:
        // a provider login another account holds leaves the address without one, and the outcome refuses it.
        var resolved = await subjects.ResolveExternalAsync(proof, ct);
        if (resolved.Subject is LoginSubject.NoAccount && !resolved.IsLinkedElsewhere)
        {
            var opened = await registrar.OpenAsync(external.ProvenEmail, ct);
            if (opened.IsFailure)
                return Result.Failure<LoginOutcome>(opened.Error);
        }

        // The outcome links the login, writes its audit row, and then opens the session. A link another account
        // won after the check above is answered there. From here on CancellationToken.None, as above.
        return Result.Success(await outcome.ResolveExternalAsync(proof, CancellationToken.None));
    }

    private static Result<LoginOutcome> GrantUnusable() => Result.Failure<LoginOutcome>(
        DomainError.Gone(AuthErrorCodes.LoginGrantUnusable, AuthErrorCodes.LoginGrantUnusableMessage));
}
