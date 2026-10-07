using FluentValidation;
using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

public sealed class ListFeedbackQueryValidator : AbstractValidator<ListFeedbackQuery>
{
    public ListFeedbackQueryValidator()
    {
        RuleFor(q => q.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, ListFeedbackQuery.MaxPageSize);
        RuleFor(q => q.Status).IsInEnum();
        RuleFor(q => q.PageKey)
            .Must(key => key is null || FeedbackPage.TryFromKey(key, out _))
            .WithMessage("Okänd sida.");
    }
}
