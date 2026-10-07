using FluentValidation;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

public sealed class GetFeedbackSummaryQueryValidator : AbstractValidator<GetFeedbackSummaryQuery>
{
    public GetFeedbackSummaryQueryValidator()
    {
        RuleFor(q => q.Days)
            .Must(GetFeedbackSummaryQuery.AllowedDays.Contains)
            .WithMessage("Välj 7, 30 eller 90 dagar.");
    }
}
