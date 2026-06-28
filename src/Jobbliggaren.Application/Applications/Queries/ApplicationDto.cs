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
    DateTimeOffset? AppliedAt,
    // #342 (ADR 0085 §3): the moment of the last status transition
    // (Application.LastStatusChangeAt). Anchors attention signal 4 (no response
    // for long) — distinct from AppliedAt (stable apply date) and UpdatedAt
    // (moves on any mutation, not just status changes).
    DateTimeOffset LastStatusChangeAt,
    // #342 (ADR 0085 §3): attention signal 2 — true when a follow-up is Pending
    // and its ScheduledAt has passed. Projected at the read boundary as a
    // correlated EXISTS (CQRS list ≠ detail — no followUps[] hydration, ADR 0048 Alt C rejected).
    bool HasOverdueFollowUp,
    // #342 (ADR 0085 §3): the per-aggregate ghosted threshold
    // (Application.GhostedThresholdDays, default 21), reused by attention signal 4.
    // Projected (not a new config threshold) so the pure Application-layer
    // evaluator can honour the per-aggregate value without a magic number.
    int GhostedThresholdDays);
