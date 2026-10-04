using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;

public sealed class CountAccountsByStatusQueryValidator : AbstractValidator<CountAccountsByStatusQuery>
{
    public CountAccountsByStatusQueryValidator()
    {
        RuleFor(q => q.Address).AccountAddressTerm();
    }
}
