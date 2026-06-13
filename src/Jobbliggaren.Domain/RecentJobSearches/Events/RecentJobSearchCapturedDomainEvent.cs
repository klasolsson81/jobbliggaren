using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.RecentJobSearches.Events;

public sealed record RecentJobSearchCapturedDomainEvent(
    RecentJobSearchId RecentJobSearchId,
    JobSeekerId JobSeekerId,
    string FilterHash,
    DateTimeOffset OccurredAt) : IDomainEvent;
