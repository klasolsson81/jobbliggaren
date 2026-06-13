using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

public sealed record ResumeVersionCreatedDomainEvent(
    ResumeId ResumeId,
    ResumeVersionId VersionId,
    ResumeVersionKind Kind,
    DateTimeOffset OccurredAt) : IDomainEvent;
