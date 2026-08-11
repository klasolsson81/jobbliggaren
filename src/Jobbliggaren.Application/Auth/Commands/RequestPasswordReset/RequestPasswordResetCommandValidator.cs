using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandValidator
    : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        // Same email rule as registration and the confirmation resend: a format-level 400 is
        // existence-independent (identical for a known and an unknown address) so it is not an
        // enumeration oracle, while any well-formed address funnels to the uniform 202 in the handler.
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
