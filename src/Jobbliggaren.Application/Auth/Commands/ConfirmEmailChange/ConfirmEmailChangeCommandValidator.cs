using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator()
    {
        // No format rule, so no message describes what a real grant looks like.
        RuleFor(c => c.ChangeEmailGrant).NotEmpty().MaximumLength(ReauthGrantRules.MaximumLength);

        // The address the grant is asserted for: well-formed and within the one email bound, so a malformed
        // request is a clean 400 before anything is redeemed.
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
    }
}
