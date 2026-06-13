using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications.Events;

public sealed record ApplicationNotedDomainEvent(
    ApplicationId ApplicationId,
    ApplicationNoteId NoteId,
    DateTimeOffset OccurredAt) : IDomainEvent;
