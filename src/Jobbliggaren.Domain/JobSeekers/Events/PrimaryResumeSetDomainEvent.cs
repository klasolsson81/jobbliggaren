using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Domain.JobSeekers.Events;

public sealed record PrimaryResumeSetDomainEvent(
    JobSeekerId JobSeekerId,
    ResumeId? NewPrimaryResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
