using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Applications.Events;

public sealed record ApplicationDeletedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
