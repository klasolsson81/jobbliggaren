using FluentValidation;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddressRules.MaximumLength);
        // Shared rule (NotEmpty + MinimumLength 12) — reconciles the floor with Identity's
        // RequiredLength = 12, replacing the stray MinimumLength(8) that let 8–11 char passwords
        // pass validation only to fail at UserManager.CreateAsync.
        RuleFor(c => c.Password).Password();
        // Caps against the aggregate's own number rather than a copy of it (#1117). The
        // personnummer rule that guards this same field is NOT duplicated here: JobSeeker
        // owns it as a structural invariant, and its refusal surfaces as a 400 through the
        // central DomainError mapper. One rule, one home.
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(JobSeeker.MaxDisplayNameLength);
        // #1736 (ADR 0142 D6): the acceptance is a REQUEST-shape requirement, refused here in the
        // validation behavior — before the handler and so before CreateUserAsync, which is what
        // leaves no Identity user behind for the #508 sweep; and it reads nothing but the flag, so
        // the response cannot vary with the address (#714). The aggregate refuses independently
        // (JobSeeker.Register on a null TermsAcceptance): "this request did not express acceptance"
        // and "a seeker cannot exist without a stamp" are two propositions, so this is ordering,
        // not a second home for one rule — the #1117 form above.
        RuleFor(c => c.AcceptTerms).Equal(true)
            .WithMessage("Du måste godkänna användarvillkoren för att skapa ett konto.");
    }
}
