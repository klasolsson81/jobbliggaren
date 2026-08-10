using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.ResetPassword;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        // Presence only. The token is a bearer credential, so a shape rule here could reject a supplied
        // one and echo it through the ValidationException path — the same reasoning that keeps a length
        // rule off ChangePasswordCommand's re-auth credential. Whether the token is valid is
        // UserManager's to decide, and every rejection there collapses to one uniform failure.
        RuleFor(c => c.Token).NotEmpty();

        // The shared strength rule (NotEmpty + MinimumLength 12), matching Identity's RequiredLength so a
        // too-short password is a clean 400 before UserManager runs. Safe to length-check: this is a NEW
        // value, not a credential being verified, and the ValidationException arm serializes only
        // property->messages, never AttemptedValue.
        RuleFor(c => c.NewPassword).Password();
    }
}
