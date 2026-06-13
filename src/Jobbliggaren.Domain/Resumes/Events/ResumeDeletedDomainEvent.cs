using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

public sealed record ResumeDeletedDomainEvent(
    ResumeId ResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
