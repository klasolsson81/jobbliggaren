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
    IReadOnlyList<FeedbackReplyDto> Replies,
    FeedbackScreenshotMetadataDto? Screenshot = null)
{
    public override string ToString() => $"FeedbackDetailDto({Id}, {Status})";
}

public sealed record FeedbackScreenshotMetadataDto(int Width, int Height);

/// <summary>One reply sent to the reporter (#2069). <see cref="State"/> is the effective state.</summary>
public sealed record FeedbackReplyDto(
    Guid Id,
    string Text,
    FeedbackReplyState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public override string ToString() => $"FeedbackReplyDto({Id}, {State}, text redacted)";
}
