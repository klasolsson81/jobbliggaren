using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobSeekers.Events;

public sealed record JobSeekerDeletedDomainEvent(
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
