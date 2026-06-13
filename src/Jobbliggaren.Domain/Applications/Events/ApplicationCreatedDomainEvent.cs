using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record ApplicationCreatedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    JobAdId? JobAdId,
    DateTimeOffset OccurredAt) : IDomainEvent;
