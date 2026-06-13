using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.SavedJobAds.Events;

public sealed record JobAdUnsavedDomainEvent(
    SavedJobAdId SavedJobAdId,
    JobSeekerId JobSeekerId,
    JobAdId JobAdId,
    DateTimeOffset OccurredAt) : IDomainEvent;
