using FluentValidation;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;

public sealed class GetPendingAccountEmailChangeQueryValidator : AbstractValidator<GetPendingAccountEmailChangeQuery>
{
    public GetPendingAccountEmailChangeQueryValidator() => RuleFor(q => q.UserId).NotEmpty();
}
