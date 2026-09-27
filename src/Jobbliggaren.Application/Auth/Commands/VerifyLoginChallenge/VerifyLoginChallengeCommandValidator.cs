using FluentValidation;
using Jobbliggaren.Application.Auth.LoginChallenges;

namespace Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;

public sealed class VerifyLoginChallengeCommandValidator : AbstractValidator<VerifyLoginChallengeCommand>
{
    public VerifyLoginChallengeCommandValidator()
    {
        RuleFor(c => c.ChallengeId).NotEmpty().MaximumLength(64);

        // Exactly the minted shape, so a malformed code is refused here and spends no attempt, and the
        // store's fixed-time compare always sees two strings of one length.
        RuleFor(c => c.Code).NotEmpty().Matches($@"^[0-9]{{{LoginChallengePolicy.CodeLength}}}\z");

        // Optional; when present, the grant token's bound, as complete's.
        RuleFor(c => c.LinkGrant).NotEmpty().MaximumLength(64).When(c => c.LinkGrant is not null);
    }
}
