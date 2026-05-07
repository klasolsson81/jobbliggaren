using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Applications.Events;

public sealed record ApplicationCreatedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    JobAdId? JobAdId,
    DateTimeOffset OccurredAt) : IDomainEvent;
