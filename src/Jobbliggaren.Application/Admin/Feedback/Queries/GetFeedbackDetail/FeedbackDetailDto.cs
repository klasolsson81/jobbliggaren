using Jobbliggaren.Domain.Feedback;

namespace Jobbliggaren.Application.Admin.Feedback.Queries.GetFeedbackDetail;

public sealed record FeedbackDetailDto(
    Guid Id,
    string PageKey,
    int? Rating,
    string? Comment,
    FeedbackStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StatusChangedAt,
    string? ReporterEmail,
    ReportedClientDto Client,
    string? AppVersion,
    FeedbackNotificationDto? Notification,
    FeedbackScreenshotMetadataDto? Screenshot = null)
{
    public override string ToString() => $"FeedbackDetailDto({Id}, {Status})";
}

public sealed record FeedbackScreenshotMetadataDto(int Width, int Height);
