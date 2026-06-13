using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

public sealed record ResumeContentUpdatedDomainEvent(
    ResumeId ResumeId,
    ResumeVersionId VersionId,
    DateTimeOffset OccurredAt) : IDomainEvent;
