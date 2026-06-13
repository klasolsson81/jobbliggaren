using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.SavedSearches.Events;

public sealed record SavedSearchCreatedDomainEvent(
    SavedSearchId SavedSearchId,
    JobSeekerId JobSeekerId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent;
