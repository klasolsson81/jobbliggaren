using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>Input-shape validation for the get-parsed-resume query (Fas 4 STEG B / B1b).</summary>
public sealed class GetParsedResumeQueryValidator : AbstractValidator<GetParsedResumeQuery>
{
    public GetParsedResumeQueryValidator()
    {
        RuleFor(q => q.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");
    }
}
