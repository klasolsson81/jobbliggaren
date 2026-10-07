using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;

public sealed class ReinstateAccountCommandValidator : AbstractValidator<ReinstateAccountCommand>
{
    public ReinstateAccountCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.ReauthGrant).ReauthGrant();
    }
}
