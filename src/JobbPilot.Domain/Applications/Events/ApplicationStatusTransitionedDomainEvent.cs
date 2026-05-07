using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Applications.Events;

public sealed record ApplicationStatusTransitionedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    ApplicationStatus Previous,
    ApplicationStatus Next,
    DateTimeOffset OccurredAt) : IDomainEvent;
