using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

public sealed class ChangeEmailCommandValidator : AbstractValidator<ChangeEmailCommand>
{
    public ChangeEmailCommandValidator()
    {
        // ValidationBehavior runs BEFORE ReauthenticationBehavior, so an empty grant is a 400 before the
        // re-auth check — empty vs wrong = 400 vs 401, revealing nothing. (Parity with DeleteAccount.)
        RuleFor(c => c.ReauthGrant).ReauthGrant();

        // The new email is a new value, not a re-auth credential: NotEmpty + well-formed + length cap,
        // so a malformed address is a clean 400 before a token is minted. The ValidationException arm
        // serializes only property->messages (never AttemptedValue), and an email address is not a
        // secret, so length/format rules are safe here.
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
    }
}
