using FluentValidation;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;

public sealed class ListJuniorJobAdsQueryValidator : AbstractValidator<ListJuniorJobAdsQuery>
{
    public ListJuniorJobAdsQueryValidator()
    {
        RuleFor(q => q.View).IsInEnum();
        RuleFor(q => q.Page).InclusiveBetween(1, ListJuniorJobAdsQuery.MaxPage);
    }
}
