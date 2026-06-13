using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record FollowUpAddedDomainEvent(
    ApplicationId ApplicationId,
    FollowUpId FollowUpId,
    DateTimeOffset OccurredAt) : IDomainEvent;
