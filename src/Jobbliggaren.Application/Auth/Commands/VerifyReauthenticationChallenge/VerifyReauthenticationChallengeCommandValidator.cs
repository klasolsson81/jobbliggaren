using FluentValidation;
using Jobbliggaren.Application.Auth.LoginChallenges;

namespace Jobbliggaren.Application.Auth.Commands.VerifyReauthenticationChallenge;

public sealed class VerifyReauthenticationChallengeCommandValidator
    : AbstractValidator<VerifyReauthenticationChallengeCommand>
{
    public VerifyReauthenticationChallengeCommandValidator()
    {
        RuleFor(c => c.ChallengeId).NotEmpty().MaximumLength(64);

        // Exactly the minted shape (the VerifyLoginChallengeCommandValidator rule): a malformed code is refused
        // here and spends no attempt, and the store's fixed-time compare always sees two strings of one length.
        RuleFor(c => c.Code).NotEmpty().Matches($@"^[0-9]{{{LoginChallengePolicy.CodeLength}}}\z");
    }
}
