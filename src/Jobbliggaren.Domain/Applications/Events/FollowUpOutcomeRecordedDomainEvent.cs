using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record FollowUpOutcomeRecordedDomainEvent(
    ApplicationId ApplicationId,
    FollowUpId FollowUpId,
    FollowUpOutcome Outcome,
    DateTimeOffset OccurredAt) : IDomainEvent;
