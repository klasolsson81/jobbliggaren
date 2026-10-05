using FluentValidation;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.CompleteAccountEmailChange;

public sealed class CompleteAccountEmailChangeCommandValidator : AbstractValidator<CompleteAccountEmailChangeCommand>
{
    public CompleteAccountEmailChangeCommandValidator()
    {
        RuleFor(c => c.CurrentEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);

        // Exactly the minted shape, so a malformed code is refused here and spends no attempt, and the store's
        // fixed-time compare always sees two strings of one length.
        RuleFor(c => c.Code).NotEmpty().Matches($@"^[0-9]{{{LoginChallengePolicy.CodeLength}}}\z");
    }
}
