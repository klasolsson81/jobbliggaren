using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator()
    {
        // ValidationBehavior runs BEFORE ReauthenticationBehavior, so a missing grant is a 400 (validation)
        // before the re-auth check ever runs — empty vs wrong = 400 vs 401, revealing nothing about the account.
        RuleFor(c => c.ReauthGrant).ReauthGrant();
    }
}
