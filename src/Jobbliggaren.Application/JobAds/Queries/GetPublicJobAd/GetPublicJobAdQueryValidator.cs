using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

public sealed class GetPublicJobAdQueryValidator : AbstractValidator<GetPublicJobAdQuery>
{
    public GetPublicJobAdQueryValidator()
    {
        RuleFor(q => q.Id)
            .NotEmpty()
            .WithMessage("Annonsens id saknas.");
    }
}
