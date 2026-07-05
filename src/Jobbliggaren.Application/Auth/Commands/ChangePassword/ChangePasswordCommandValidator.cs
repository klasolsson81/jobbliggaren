using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        // The current password is the re-auth credential: NotEmpty ONLY. A length/complexity rule
        // here could fail on a non-empty supplied credential and echo it through the
        // ValidationException path, and the strength of an EXISTING password is irrelevant.
        // ValidationBehavior runs BEFORE ReauthenticationBehavior, so an empty current password is a
        // 400 (validation) before the re-auth check runs — empty vs wrong = 400 vs 401, revealing
        // nothing about the account. (Parity with DeleteAccountCommandValidator.)
        RuleFor(c => c.CurrentPassword).NotEmpty();

        // The new password uses the shared strength rule (NotEmpty + MinimumLength 12), matching
        // Identity's RequiredLength so a weak new password is a clean 400 before UserManager runs.
        // Safe to length-check here: NewPassword is a new value, not a re-auth credential, and the
        // ValidationException arm serializes only property->messages (never AttemptedValue).
        RuleFor(c => c.NewPassword).Password();
    }
}
