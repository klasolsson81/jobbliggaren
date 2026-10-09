using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;

public sealed class SearchAccountsQueryValidator : AbstractValidator<SearchAccountsQuery>
{
    public SearchAccountsQueryValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, SearchAccountsQuery.MaxPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, SearchAccountsQuery.MaxPageSize);
        RuleFor(q => q.Address).AccountAddressTerm();
        RuleFor(q => q).Must(q => q.RegisteredFrom.HasValue == q.RegisteredBefore.HasValue
            && (!q.RegisteredFrom.HasValue || q.RegisteredFrom <= q.RegisteredBefore))
            .WithMessage("Registration bounds must be a complete ordered pair.");
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.Sort).IsInEnum();
    }
}
