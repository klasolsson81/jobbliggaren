namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackSummary;

public sealed record FeedbackSummaryDto(int Days, IReadOnlyList<FeedbackPageSummary> Pages);
