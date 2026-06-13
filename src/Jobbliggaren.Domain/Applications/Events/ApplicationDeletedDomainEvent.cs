using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record ApplicationDeletedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
