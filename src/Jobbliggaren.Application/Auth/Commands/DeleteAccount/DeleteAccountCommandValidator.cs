using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator()
    {
        // Re-auth password required (parity with VerifyCredentialsQueryValidator). ValidationBehavior
        // runs BEFORE ReauthenticationBehavior, so a missing password is a 400 (validation) before the
        // re-auth check ever runs — empty vs wrong = 400 vs 401, revealing nothing about the account.
        RuleFor(c => c.Password).NotEmpty();
    }
}
