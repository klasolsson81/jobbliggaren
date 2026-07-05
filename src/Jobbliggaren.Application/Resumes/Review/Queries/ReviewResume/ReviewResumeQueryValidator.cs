using FluentValidation;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;

/// <summary>
/// Input-shape validation for the canonical CV review query (Fas 4b PR-4). Parity with
/// <c>ReviewParsedResumeQueryValidator</c>: non-empty id + fail-loud profile parse
/// (a bad profile is a client bug, never silently coerced to a default).
/// </summary>
public sealed class ReviewResumeQueryValidator : AbstractValidator<ReviewResumeQuery>
{
    public ReviewResumeQueryValidator()
    {
        RuleFor(q => q.ResumeId)
            .NotEmpty().WithMessage("ResumeId krävs.");

        RuleFor(q => q.Profile)
            .Must(RenderProfileNames.IsValidName)
            .WithMessage("Profilen måste vara 'Ats' eller 'Visual'.");
    }
}
