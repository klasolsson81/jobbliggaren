using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

public sealed class GetPublicJuniorItJobAdsQueryValidator : AbstractValidator<GetPublicJuniorItJobAdsQuery>
{
    public GetPublicJuniorItJobAdsQueryValidator()
    {
        RuleFor(q => q.Page)
            .InclusiveBetween(1, PublicJuniorItJobAdLimits.MaxPage)
            .WithMessage("Sidan finns inte i listan över juniora IT-jobb.");

        RuleFor(q => q.SortBy)
            .Must(s => PublicJuniorItJobAdLimits.Orders.Contains(s))
            .WithMessage("Listan kan sorteras på publiceringsdatum eller sista ansökningsdag.");

        When(q => q.Municipality is not null, () =>
        {
            RuleFor(q => q.Municipality)
                .MaximumLength(PublicJuniorItJobAdLimits.MaxMunicipalityLength)
                .WithMessage("Orten finns inte.")
                .Matches("^[A-Za-z0-9_]+$")
                .WithMessage("Orten finns inte.");
        });
    }
}
