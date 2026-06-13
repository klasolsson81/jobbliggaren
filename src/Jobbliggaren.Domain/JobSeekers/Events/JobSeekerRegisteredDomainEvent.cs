using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers.Events;

public sealed record JobSeekerRegisteredDomainEvent(
    JobSeekerId JobSeekerId,
    Guid UserId,
    string DisplayName,
    DateTimeOffset OccurredAt) : IDomainEvent;
