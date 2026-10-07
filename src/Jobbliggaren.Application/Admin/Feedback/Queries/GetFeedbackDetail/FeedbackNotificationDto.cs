using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;

public sealed record FeedbackNotificationDto(
    FeedbackNotificationState State,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? StateChangedAt);
