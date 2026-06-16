using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Domain.Applications.Events;

/// <summary>
/// Raised when a job seeker links the exact CV version used for an application
/// (F4-11, BUILD §5.3). The <see cref="ResumeVersionId"/> belongs to the owner's
/// own <c>Resume</c> — cross-user references are rejected upstream in the handler.
/// </summary>
public sealed record ApplicationResumeVersionAttachedDomainEvent(
    ApplicationId ApplicationId,
    JobSeekerId JobSeekerId,
    ResumeVersionId ResumeVersionId,
    DateTimeOffset OccurredAt) : IDomainEvent;
