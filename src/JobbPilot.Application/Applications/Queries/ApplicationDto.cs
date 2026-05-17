namespace JobbPilot.Application.Applications.Queries;

public sealed record ApplicationDto(
    Guid Id,
    Guid JobSeekerId,
    Guid? JobAdId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JobAdSummaryDto? JobAd);
