using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record ApplicationStatusTransitionedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    ApplicationStatus Previous,
    ApplicationStatus Next,
    DateTimeOffset OccurredAt) : IDomainEvent;
