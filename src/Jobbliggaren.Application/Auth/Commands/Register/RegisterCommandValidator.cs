using FluentValidation;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.JobSeekers;

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
        // Caps against the aggregate's own number rather than a copy of it (#1117). The
        // personnummer rule that guards this same field is NOT duplicated here: JobSeeker
        // owns it as a structural invariant, and its refusal surfaces as a 400 through the
        // central DomainError mapper. One rule, one home.
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(JobSeeker.MaxDisplayNameLength);
    }
}
