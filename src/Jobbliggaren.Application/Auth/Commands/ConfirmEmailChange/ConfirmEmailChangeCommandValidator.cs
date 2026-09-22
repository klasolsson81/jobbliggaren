using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator()
    {
        // The grant token's bound is CompleteLoginChallengeCommandValidator's; no format rule, so no message
        // describes what a real grant looks like.
        RuleFor(c => c.ChangeEmailGrant).NotEmpty().MaximumLength(64);

        // The address the grant is asserted for: well-formed and within the one email bound, so a malformed
        // request is a clean 400 before anything is redeemed.
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
    }
}
