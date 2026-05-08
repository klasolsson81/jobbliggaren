using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;

namespace JobbPilot.Domain.Resumes.Events;

public sealed record ResumeCreatedDomainEvent(
    ResumeId ResumeId,
    JobSeekerId JobSeekerId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent;
