using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Applications.Events;

public sealed record FollowUpAddedDomainEvent(
    ApplicationId ApplicationId,
    FollowUpId FollowUpId,
    DateTimeOffset OccurredAt) : IDomainEvent;
