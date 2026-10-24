using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;

public sealed class GetRelatedPublicJobAdsQueryValidator : AbstractValidator<GetRelatedPublicJobAdsQuery>
{
    public GetRelatedPublicJobAdsQueryValidator()
    {
        RuleFor(q => q.Id)
            .NotEmpty()
            .WithMessage("Annonsens id saknas.");
    }
}
