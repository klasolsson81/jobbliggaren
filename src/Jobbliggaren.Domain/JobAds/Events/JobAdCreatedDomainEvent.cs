using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds.Events;

public sealed record JobAdCreatedDomainEvent(
    JobAdId JobAdId,
    string Title,
    DateTimeOffset OccurredAt) : IDomainEvent;
