using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds.Events;

public sealed record JobAdArchivedDomainEvent(
    JobAdId JobAdId,
    DateTimeOffset OccurredAt) : IDomainEvent;
