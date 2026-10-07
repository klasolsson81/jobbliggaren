using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Feedback.Events;

public sealed record FeedbackSubmittedDomainEvent(
    FeedbackSubmissionId SubmissionId, FeedbackPage Page, DateTimeOffset OccurredAt) : IDomainEvent;
