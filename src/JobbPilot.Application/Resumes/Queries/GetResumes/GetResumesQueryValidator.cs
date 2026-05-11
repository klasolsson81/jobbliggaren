using FluentValidation;

namespace JobbPilot.Application.Resumes.Queries.GetResumes;

public sealed class GetResumesQueryValidator : AbstractValidator<GetResumesQuery>
{
    public GetResumesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
