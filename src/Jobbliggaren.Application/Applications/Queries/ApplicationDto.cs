namespace Jobbliggaren.Application.Applications.Queries;

public sealed record ApplicationDto(
    Guid Id,
    Guid JobSeekerId,
    Guid? JobAdId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JobAdSummaryDto? JobAd,
    // #336: the date the application was first submitted (Application.AppliedAt,
    // idempotent on first Submit). null for Draft (never submitted). Drives the
    // relative "Skickad för X dagar sedan" row tag — UpdatedAt would be untrue
    // (it moves on every status change).
    DateTimeOffset? AppliedAt);
