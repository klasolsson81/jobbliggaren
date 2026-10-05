using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;

public sealed class RequestAccountEmailChangeCommandValidator : AbstractValidator<RequestAccountEmailChangeCommand>
{
    public RequestAccountEmailChangeCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        // ValidationBehavior runs before ReauthenticationBehavior, so an empty grant is a 400 before the re-auth
        // check, the rule every re-authenticating request shares.
        RuleFor(c => c.ReauthGrant).ReauthGrant();

        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
    }
}
