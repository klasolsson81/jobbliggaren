using FluentValidation;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Commands.ChangeTemplateOptions;

// SmartEnum-driven validation: every member must resolve to its closed set by Name — no
// hardcoded name lists / magic strings (CLAUDE.md §5). Cascade.Stop guards the .Must
// resolver from a null/empty name (SmartEnum.TryFromName throws on a null dictionary key),
// so a missing field yields the single NotEmpty message, not two errors.
public sealed class ChangeTemplateOptionsCommandValidator
    : AbstractValidator<ChangeTemplateOptionsCommand>
{
    public ChangeTemplateOptionsCommandValidator()
    {
        RuleFor(c => c.ResumeId)
            .NotEqual(Guid.Empty).WithMessage("CV-id krävs.");

        RuleFor(c => c.Template)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mall krävs.")
            .Must(n => CvTemplate.TryFromName(n, out _)).WithMessage("Ogiltig mall.");

        RuleFor(c => c.AccentColor)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Accentfärg krävs.")
            .Must(n => CvAccentColor.TryFromName(n, out _)).WithMessage("Ogiltig accentfärg.");

        RuleFor(c => c.FontPair)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Typsnitt krävs.")
            .Must(n => CvFontPair.TryFromName(n, out _)).WithMessage("Ogiltigt typsnitt.");

        RuleFor(c => c.Density)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Täthet krävs.")
            .Must(n => CvDensity.TryFromName(n, out _)).WithMessage("Ogiltig täthet.");
    }
}
