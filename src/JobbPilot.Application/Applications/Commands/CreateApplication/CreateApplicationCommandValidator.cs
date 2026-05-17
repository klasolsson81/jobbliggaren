using FluentValidation;

namespace JobbPilot.Application.Applications.Commands.CreateApplication;

public sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(c => c.CoverLetter)
            .MaximumLength(10_000)
            .When(c => c.CoverLetter is not null)
            .WithMessage("Personligt brev får vara max 10 000 tecken.");

        // Defense-in-depth: speglar domän-invarianten
        // Application.JobAdAndManualMutuallyExclusive i validator-lagret.
        RuleFor(c => c.Manual)
            .Null()
            .When(c => c.JobAdId.HasValue)
            .WithMessage("En ansökan kan inte vara både kopplad till en annons och manuellt angiven.");

        // Manuell ansökan (ingen JobAd-koppling): Jobbtitel + Företag obligatoriska.
        When(c => c.JobAdId is null && c.Manual is not null, () =>
        {
            RuleFor(c => c.Manual!.Title)
                .NotEmpty()
                .WithMessage("Jobbtitel är obligatorisk.");
            RuleFor(c => c.Manual!.Company)
                .NotEmpty()
                .WithMessage("Företag är obligatoriskt.");
        });
    }
}
