using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Resumes.Events;

public sealed record ResumeContentUpdatedDomainEvent(
    ResumeId ResumeId,
    ResumeVersionId VersionId,
    DateTimeOffset OccurredAt) : IDomainEvent;
