using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Resumes.Events;

public sealed record ResumeCreatedDomainEvent(
    ResumeId ResumeId,
    JobSeekerId JobSeekerId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent;
