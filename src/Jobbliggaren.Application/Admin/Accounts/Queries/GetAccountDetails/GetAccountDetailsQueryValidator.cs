using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;

public sealed class GetAccountDetailsQueryValidator : AbstractValidator<GetAccountDetailsQuery>
{
    public GetAccountDetailsQueryValidator()
    {
        RuleFor(q => q.UserId).NotEmpty();
    }
}
