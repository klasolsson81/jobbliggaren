using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

/// <summary>
/// Raised when an imported CV's design is adopted ("Adoptera min design", Fas 4b —
/// CTO-bind D9, ADR 0096). One-way: <see cref="Resume.Adopt"/> stamps
/// <c>Resume.AdoptedAt</c> exactly once. Ids + timestamp only, never CV content
/// (ADR 0074 Invariant 3). Raise-only/in-memory like every domain event here; the
/// audit row for the adopt USER ACTION comes from an <c>IAuditableCommand</c> when
/// the Fas C adopt flow (PR-11) wires the command — not from this event.
/// </summary>
public sealed record ResumeAdoptedDomainEvent(
    ResumeId ResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
