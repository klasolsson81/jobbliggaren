using FluentValidation;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;

/// <summary>
/// Input-shape validation for the render-by-Resume-id query (TD-112 / #202). The
/// <c>ResumeId</c> must be a non-empty Guid and the <c>Profile</c> string must parse fail-loud to
/// a <see cref="RenderProfile"/> member (case-sensitive exact match — parity
/// <c>RenderCvQueryValidator</c>). "Both" is a <c>RubricProfile</c> member, NOT a
/// <see cref="RenderProfile"/>, so it is rejected here.
/// </summary>
public sealed class RenderResumeQueryValidator : AbstractValidator<RenderResumeQuery>
{
    public RenderResumeQueryValidator()
    {
        RuleFor(q => q.ResumeId)
            .NotEmpty().WithMessage("ResumeId krävs.");

        RuleFor(q => q.Profile)
            .Must(p => Enum.TryParse<RenderProfile>(p, ignoreCase: false, out _))
            .WithMessage("Profilen måste vara 'Ats' eller 'Visual'.");
    }
}
