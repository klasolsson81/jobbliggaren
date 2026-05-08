using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Resumes.Events;

public sealed record ResumeVersionCreatedDomainEvent(
    ResumeId ResumeId,
    ResumeVersionId VersionId,
    ResumeVersionKind Kind,
    DateTimeOffset OccurredAt) : IDomainEvent;
