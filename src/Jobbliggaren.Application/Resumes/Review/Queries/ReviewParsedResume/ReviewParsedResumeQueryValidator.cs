using FluentValidation;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Input-shape validation for the CV review query (Fas 4 STEG 9, F4-9). The
/// <c>ParsedResumeId</c> must be a non-empty Guid and the <c>Profile</c> string must
/// parse fail-loud to a <see cref="RenderProfile"/> member (case-sensitive exact match —
/// a bad profile is a client bug, never silently coerced to a default; parity with
/// <c>RubricVersion.Parse</c>'s fail-loud discipline). "Both" is a <c>RubricProfile</c>
/// member, NOT a <see cref="RenderProfile"/>, so it is rejected here.
/// </summary>
public sealed class ReviewParsedResumeQueryValidator : AbstractValidator<ReviewParsedResumeQuery>
{
    public ReviewParsedResumeQueryValidator()
    {
        RuleFor(q => q.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");

        RuleFor(q => q.Profile)
            .Must(p => Enum.TryParse<RenderProfile>(p, ignoreCase: false, out _))
            .WithMessage("Profilen måste vara 'Ats' eller 'Visual'.");
    }
}
