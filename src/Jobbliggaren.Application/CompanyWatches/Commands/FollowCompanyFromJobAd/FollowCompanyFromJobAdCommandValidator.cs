using FluentValidation;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;

/// <summary>
/// Defense-in-depth pre-handler surface (ValidationBehavior). The JobAdId must be a non-empty Guid;
/// existence + employer-org.nr presence are resolved in the handler (a 404/400 there, not a 400 here).
/// </summary>
public sealed class FollowCompanyFromJobAdCommandValidator : AbstractValidator<FollowCompanyFromJobAdCommand>
{
    public FollowCompanyFromJobAdCommandValidator()
    {
        RuleFor(c => c.JobAdId)
            .NotEmpty()
            .WithMessage("JobAdId krävs.");
    }
}
