using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.ListFeedback;

public sealed record FeedbackListItemDto(
    Guid Id,
    string PageKey,
    int? Rating,
    string? Excerpt,
    FeedbackStatus Status,
    DateTimeOffset SubmittedAt,
    FeedbackNotificationState? NotificationState);
