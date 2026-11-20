using FluentValidation;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;

public sealed class DecideJuniorJobAdCommandValidator : AbstractValidator<DecideJuniorJobAdCommand>
{
    public DecideJuniorJobAdCommandValidator()
    {
        RuleFor(c => c.JobAdId).NotEmpty();
        RuleFor(c => c.Decision).IsInEnum().When(c => c.Decision is not null);
    }
}
