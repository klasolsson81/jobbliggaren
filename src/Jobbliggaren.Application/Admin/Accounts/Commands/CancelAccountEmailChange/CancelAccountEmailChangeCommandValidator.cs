using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;

public sealed class CancelAccountEmailChangeCommandValidator : AbstractValidator<CancelAccountEmailChangeCommand>
{
    public CancelAccountEmailChangeCommandValidator() => RuleFor(c => c.UserId).NotEmpty();
}
