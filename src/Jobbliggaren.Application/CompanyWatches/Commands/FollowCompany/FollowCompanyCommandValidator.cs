using FluentValidation;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;

/// <summary>
/// Defense-in-depth pre-handler surface (ValidationBehavior). Delegates the org.nr format to the
/// <see cref="OrganizationNumber"/> VO (single source of truth — no duplicated regex; FORK A1).
/// </summary>
public sealed class FollowCompanyCommandValidator : AbstractValidator<FollowCompanyCommand>
{
    public FollowCompanyCommandValidator()
    {
        RuleFor(c => c.OrganizationNumber)
            .Must(v => OrganizationNumber.Create(v).IsSuccess)
            .WithMessage("Organisationsnummer måste vara 10 siffror.");
    }
}
