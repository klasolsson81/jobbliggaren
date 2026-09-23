using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;

public sealed class CompleteLoginChallengeCommandValidator : AbstractValidator<CompleteLoginChallengeCommand>
{
    public CompleteLoginChallengeCommandValidator()
    {
        RuleFor(c => c.GrantToken).NotEmpty().MaximumLength(64);

        // Refused here, in the validation behavior, and so before the handler redeems anything: a request
        // without the acceptance leaves the grant usable, and no account behind (ADR 0142 D6, the form
        // RegisterCommandValidator has).
        RuleFor(c => c.AcceptTerms).Equal(true)
            .WithMessage("Du måste godkänna användarvillkoren för att skapa ett konto.");
    }
}
