namespace Jobbliggaren.Application.Applications.Queries;

public sealed record ApplicationDetailDto(
    Guid Id,
    Guid JobSeekerId,
    Guid? JobAdId,
    string Status,
    string? CoverLetter,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FollowUpDto> FollowUps,
    IReadOnlyList<NoteDto> Notes,
    JobAdSummaryDto? JobAd);
