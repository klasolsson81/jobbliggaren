using FluentValidation;

namespace JobbPilot.Application.Applications.Queries.GetApplications;

public sealed class GetApplicationsQueryValidator : AbstractValidator<GetApplicationsQuery>
{
    public GetApplicationsQueryValidator()
    {
        RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
