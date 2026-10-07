namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

/// <summary>Counts per status inside the page filter, so a filter label never shows a count it did not take.</summary>
public sealed record FeedbackStatusCountsDto(int All, int New, int InProgress, int Resolved, int Declined);
