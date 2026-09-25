using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;

public sealed class RequestLoginChallengeCommandValidator : AbstractValidator<RequestLoginChallengeCommand>
{
    public RequestLoginChallengeCommandValidator()
    {
        // The house email rule: a format-level 400 is identical for a known and an unknown address, so it is
        // not an enumeration oracle.
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
    }
}
