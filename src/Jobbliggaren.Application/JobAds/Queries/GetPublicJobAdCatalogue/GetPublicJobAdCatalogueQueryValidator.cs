using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

public sealed class GetPublicJobAdCatalogueQueryValidator : AbstractValidator<GetPublicJobAdCatalogueQuery>
{
    public GetPublicJobAdCatalogueQueryValidator()
    {
        RuleFor(q => q.Page)
            .InclusiveBetween(1, PublicJobAdCatalogueLimits.MaxPage)
            .WithMessage("Sidan finns inte i listan över lediga jobb.");
    }
}
