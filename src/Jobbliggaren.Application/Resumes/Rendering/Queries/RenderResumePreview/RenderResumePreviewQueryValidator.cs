using FluentValidation;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResumePreview;

// SmartEnum-driven validation of the ephemeral preview's four visual options — every member must
// resolve to its closed set by Name (no hardcoded name lists / magic strings, CLAUDE.md §5).
// Cascade.Stop guards the .Must resolver from a null/empty name (SmartEnum.TryFromName throws on a
// null dictionary key), so a missing field yields the single NotEmpty message. Deliberately mirrors
// ChangeTemplateOptionsCommandValidator (the write-path validator) — the preview and the save reject
// identical bad input identically, so a set of options that previews is exactly a set that saves.
public sealed class RenderResumePreviewQueryValidator : AbstractValidator<RenderResumePreviewQuery>
{
    public RenderResumePreviewQueryValidator()
    {
        RuleFor(q => q.ResumeId)
            .NotEqual(Guid.Empty).WithMessage("CV-id krävs.");

        RuleFor(q => q.Template)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Mall krävs.")
            .Must(n => CvTemplate.TryFromName(n, out _)).WithMessage("Ogiltig mall.");

        RuleFor(q => q.AccentColor)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Accentfärg krävs.")
            .Must(n => CvAccentColor.TryFromName(n, out _)).WithMessage("Ogiltig accentfärg.");

        RuleFor(q => q.FontPair)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Typsnitt krävs.")
            .Must(n => CvFontPair.TryFromName(n, out _)).WithMessage("Ogiltigt typsnitt.");

        RuleFor(q => q.Density)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Täthet krävs.")
            .Must(n => CvDensity.TryFromName(n, out _)).WithMessage("Ogiltig täthet.");
    }
}
