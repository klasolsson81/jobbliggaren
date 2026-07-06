using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

public sealed class ChangeEmailCommandValidator : AbstractValidator<ChangeEmailCommand>
{
    // Identity's EmailAddressAttribute / NormalizedEmail column and Resend both stay well within this;
    // a generous cap that still bounds the input before UserManager runs.
    private const int MaxEmailLength = 256;

    public ChangeEmailCommandValidator()
    {
        // The current password is the re-auth credential: NotEmpty ONLY. A length/complexity rule here
        // could fail on a non-empty supplied credential and echo it through the ValidationException
        // path, and the strength of an EXISTING password is irrelevant. ValidationBehavior runs BEFORE
        // ReauthenticationBehavior, so an empty current password is a 400 before the re-auth check —
        // empty vs wrong = 400 vs 401, revealing nothing. (Parity with ChangePassword / DeleteAccount.)
        RuleFor(c => c.CurrentPassword).NotEmpty();

        // The new email is a new value, not a re-auth credential: NotEmpty + well-formed + length cap,
        // so a malformed address is a clean 400 before a token is minted. The ValidationException arm
        // serializes only property->messages (never AttemptedValue), and an email address is not a
        // secret, so length/format rules are safe here.
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(MaxEmailLength);
    }
}
