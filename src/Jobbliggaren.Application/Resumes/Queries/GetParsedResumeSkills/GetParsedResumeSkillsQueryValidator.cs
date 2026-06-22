using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

/// <summary>Input-shape validation for the get-parsed-resume-skills query (ADR 0079 STEG 3).</summary>
public sealed class GetParsedResumeSkillsQueryValidator
    : AbstractValidator<GetParsedResumeSkillsQuery>
{
    public GetParsedResumeSkillsQueryValidator()
    {
        RuleFor(q => q.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");
    }
}
