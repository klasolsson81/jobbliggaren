using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.RecentJobSearches.Events;

public sealed record RecentJobSearchQuickLinkUnmarkedDomainEvent(
    RecentJobSearchId RecentJobSearchId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
