namespace Jobbliggaren.Application.Feedback.Queries.GetFeedbackPromptState;

public sealed record FeedbackPromptStateDto(bool Open, IReadOnlyList<string> AnsweredPages);
