using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Resumes.Events;

public sealed record ResumeDeletedDomainEvent(
    ResumeId ResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
