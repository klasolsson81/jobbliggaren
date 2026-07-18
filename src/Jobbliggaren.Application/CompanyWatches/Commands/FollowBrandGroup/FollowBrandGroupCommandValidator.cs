using FluentValidation;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;

/// <summary>
/// Defense-in-depth pre-handler surface (ValidationBehavior). Delegates the slug FORMAT to the
/// <see cref="BrandGroupId"/> VO (single source of truth — no duplicated regex). EXISTENCE in the
/// curated catalogue is the handler's concern (a 404, not a 400).
/// </summary>
public sealed class FollowBrandGroupCommandValidator : AbstractValidator<FollowBrandGroupCommand>
{
    public FollowBrandGroupCommandValidator()
    {
        RuleFor(c => c.BrandGroupId)
            .Must(v => BrandGroupId.Create(v).IsSuccess)
            .WithMessage("Varumärkesgrupp måste vara en giltig slug (gemener a–z, siffror, bindestreck).");
    }
}
