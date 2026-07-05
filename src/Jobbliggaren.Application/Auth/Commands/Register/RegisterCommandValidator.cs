using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // Shared rule (NotEmpty + MinimumLength 12) — reconciles the floor with Identity's
        // RequiredLength = 12, replacing the stray MinimumLength(8) that let 8–11 char passwords
        // pass validation only to fail at UserManager.CreateAsync.
        RuleFor(c => c.Password).Password();
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
    }
}
