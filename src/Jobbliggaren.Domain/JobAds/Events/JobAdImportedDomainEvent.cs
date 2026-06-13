using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds.Events;

public sealed record JobAdImportedDomainEvent(
    JobAdId JobAdId,
    string Source,
    string ExternalId,
    string Title,
    DateTimeOffset OccurredAt) : IDomainEvent;
