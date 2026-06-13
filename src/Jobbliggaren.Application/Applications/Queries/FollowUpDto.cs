namespace Jobbliggaren.Application.Applications.Queries;

public sealed record FollowUpDto(
    Guid Id,
    string Channel,
    DateTimeOffset ScheduledAt,
    string? Note,
    string Outcome,
    DateTimeOffset? OutcomeAt,
    DateTimeOffset CreatedAt);
