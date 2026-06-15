using FluentValidation;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;

/// <summary>
/// Input-shape validation for the CV-render query (Fas 4 STEG 10, F4-10). The
/// <c>ParsedResumeId</c> must be a non-empty Guid and the <c>Profile</c> string must parse
/// fail-loud to a <see cref="RenderProfile"/> member (case-sensitive exact match — parity
/// <c>ReviewParsedResumeQueryValidator</c>). "Both" is a <c>RubricProfile</c> member, NOT a
/// <see cref="RenderProfile"/>, so it is rejected here.
/// </summary>
public sealed class RenderCvQueryValidator : AbstractValidator<RenderCvQuery>
{
    public RenderCvQueryValidator()
    {
        RuleFor(q => q.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");

        RuleFor(q => q.Profile)
            .Must(p => Enum.TryParse<RenderProfile>(p, ignoreCase: false, out _))
            .WithMessage("Profilen måste vara 'Ats' eller 'Visual'.");
    }
}
