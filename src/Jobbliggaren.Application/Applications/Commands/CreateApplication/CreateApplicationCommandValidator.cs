using FluentValidation;

namespace Jobbliggaren.Application.Applications.Commands.CreateApplication;

public sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(c => c.CoverLetter)
            .MaximumLength(10_000)
            .When(c => c.CoverLetter is not null)
            .WithMessage("Personligt brev får vara max 10 000 tecken.");

        // #315 (ADR 0086 D2): a JobAd-linked application must carry a captured
        // ad-text snapshot, which only the dedicated "Har ansökt" path
        // (POST /from-job-ad/{jobAdId} → CreateApplicationFromJobAdCommand)
        // produces. This generic create is fail-closed on a JobAdId so it can
        // never produce an un-snapshotted JobAd link. The FE never sends a
        // JobAdId here (the manual form sends Manual). The domain Application.Create
        // still permits a JobAdId (degenerate/test use); the product rule lives at
        // this contract boundary, not the aggregate.
        RuleFor(c => c.JobAdId)
            .Null()
            .WithMessage(
                "En ansökan kopplad till en annons skapas via knappen \"Har ansökt\" på annonsen.");

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
