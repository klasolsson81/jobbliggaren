using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

public sealed record ResumeLanguageChangedDomainEvent(
    ResumeId ResumeId,
    ResumeLanguage NewLanguage,
    DateTimeOffset OccurredAt) : IDomainEvent;
