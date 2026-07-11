using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;

public sealed class ResendEmailConfirmationCommandValidator
    : AbstractValidator<ResendEmailConfirmationCommand>
{
    public ResendEmailConfirmationCommandValidator()
    {
        // Same email rule as registration (RegisterCommandValidator): a format-level 400 is existence-
        // independent (identical for a taken and a fresh address) so it is not an enumeration oracle,
        // while any well-formed address funnels to the uniform 202 in the handler.
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
