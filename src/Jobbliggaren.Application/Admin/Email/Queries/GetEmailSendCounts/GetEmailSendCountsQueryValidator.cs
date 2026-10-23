using FluentValidation;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailSendCounts;

public sealed class GetEmailSendCountsQueryValidator : AbstractValidator<GetEmailSendCountsQuery>
{
    public GetEmailSendCountsQueryValidator()
    {
        RuleFor(q => q.Period).NotNull().IsInEnum();
    }
}
