using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.JobSeekers.Events;

public sealed record JobSeekerDeletedDomainEvent(
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
