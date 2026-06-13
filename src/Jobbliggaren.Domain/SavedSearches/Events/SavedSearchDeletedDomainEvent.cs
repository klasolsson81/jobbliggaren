using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.SavedSearches.Events;

public sealed record SavedSearchDeletedDomainEvent(
    SavedSearchId SavedSearchId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
