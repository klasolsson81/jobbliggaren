using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record ApplicationGhostedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    ApplicationStatus Previous,
    DateTimeOffset OccurredAt) : IDomainEvent;
