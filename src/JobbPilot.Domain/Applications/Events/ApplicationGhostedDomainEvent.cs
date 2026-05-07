using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Applications.Events;

public sealed record ApplicationGhostedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    ApplicationStatus Previous,
    DateTimeOffset OccurredAt) : IDomainEvent;
