using FluentValidation;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailRecentFailures;

public sealed class GetEmailRecentFailuresQueryValidator : AbstractValidator<GetEmailRecentFailuresQuery>
{
    public GetEmailRecentFailuresQueryValidator()
    {
        RuleFor(q => q.Period).NotNull().IsInEnum();
    }
}
