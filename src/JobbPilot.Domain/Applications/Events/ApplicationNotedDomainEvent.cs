using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Applications.Events;

public sealed record ApplicationNotedDomainEvent(
    ApplicationId ApplicationId,
    ApplicationNoteId NoteId,
    DateTimeOffset OccurredAt) : IDomainEvent;
