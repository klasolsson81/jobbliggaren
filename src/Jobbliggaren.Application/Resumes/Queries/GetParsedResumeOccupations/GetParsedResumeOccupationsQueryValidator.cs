using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;

/// <summary>Input-shape validation for the get-parsed-resume-occupations query (Fas 4 onboarding).</summary>
public sealed class GetParsedResumeOccupationsQueryValidator
    : AbstractValidator<GetParsedResumeOccupationsQuery>
{
    public GetParsedResumeOccupationsQueryValidator()
    {
        RuleFor(q => q.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");
    }
}
