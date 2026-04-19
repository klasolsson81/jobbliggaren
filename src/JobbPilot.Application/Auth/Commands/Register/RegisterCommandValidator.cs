using FluentValidation;

namespace JobbPilot.Application.Auth.Commands.Register;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
    }
}
