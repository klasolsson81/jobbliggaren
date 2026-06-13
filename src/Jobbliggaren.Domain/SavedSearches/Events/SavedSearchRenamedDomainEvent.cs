using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.SavedSearches.Events;

public sealed record SavedSearchRenamedDomainEvent(
    SavedSearchId SavedSearchId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent;
