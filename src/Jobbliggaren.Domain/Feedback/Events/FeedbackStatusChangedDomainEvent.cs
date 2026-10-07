using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Feedback.Events;

public sealed record FeedbackStatusChangedDomainEvent(
    FeedbackSubmissionId SubmissionId, FeedbackStatus From, FeedbackStatus To, DateTimeOffset OccurredAt) : IDomainEvent;
