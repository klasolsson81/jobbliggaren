using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Applications.Events;

public sealed record FollowUpOutcomeRecordedDomainEvent(
    ApplicationId ApplicationId,
    FollowUpId FollowUpId,
    FollowUpOutcome Outcome,
    DateTimeOffset OccurredAt) : IDomainEvent;
