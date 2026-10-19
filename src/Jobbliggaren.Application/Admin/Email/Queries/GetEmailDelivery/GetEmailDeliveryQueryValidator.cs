using FluentValidation;

namespace Jobbliggaren.Application.Admin.Email.Queries.GetEmailDelivery;

public sealed class GetEmailDeliveryQueryValidator : AbstractValidator<GetEmailDeliveryQuery>
{
    public GetEmailDeliveryQueryValidator()
    {
        RuleFor(q => q.Period).NotNull().IsInEnum();
    }
}
