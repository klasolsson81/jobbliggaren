using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;

public sealed class CountAccountsByStatusQueryValidator : AbstractValidator<CountAccountsByStatusQuery>
{
    public CountAccountsByStatusQueryValidator()
    {
        RuleFor(q => q.Address).AccountAddressTerm();
        RuleFor(q => q).Must(q => q.RegisteredFrom.HasValue == q.RegisteredBefore.HasValue
            && (!q.RegisteredFrom.HasValue || q.RegisteredFrom <= q.RegisteredBefore))
            .WithMessage("Registration bounds must be a complete ordered pair.");
    }
}
