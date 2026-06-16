using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;

public sealed class PromoteParsedResumeCommandValidator : AbstractValidator<PromoteParsedResumeCommand>
{
    public PromoteParsedResumeCommandValidator()
    {
        RuleFor(c => c.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Namn på CV är obligatoriskt.")
            .MaximumLength(200).WithMessage("Namn får vara max 200 tecken.");

        RuleFor(c => c.Content)
            .NotNull().WithMessage("Innehåll krävs.");

        RuleFor(c => c.Content.PersonalInfo)
            .NotNull().WithMessage("Personuppgifter krävs.")
            .When(c => c.Content is not null);

        RuleFor(c => c.Content.PersonalInfo.FullName)
            .NotEmpty().WithMessage("Fullständigt namn krävs.")
            .MaximumLength(200).WithMessage("Fullständigt namn får vara max 200 tecken.")
            .When(c => c.Content?.PersonalInfo is not null);

        RuleFor(c => c.Content.Summary)
            .MaximumLength(2_000).WithMessage("Sammanfattning får vara max 2 000 tecken.")
            .When(c => c.Content?.Summary is not null);
    }
}
