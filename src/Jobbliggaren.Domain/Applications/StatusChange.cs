using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications;

/// <summary>
/// An append-only record of a single status transition on an
/// <see cref="Application"/> (from → to, at the moment it happened). Its facts
/// (From/To/ChangedAt) are set once at creation and never change; only the
/// soft-delete tombstone (<see cref="DeletedAt"/>) mutates, parity with
/// <see cref="FollowUp"/>/<see cref="ApplicationNote"/>. The aggregate
/// keeps only the current <see cref="Application.Status"/> plus the overwritten
/// <see cref="Application.LastStatusChangeAt"/> scalar, so before this type there
/// was no history of intermediate transitions (ADR 0092 D4; the reason the stats
/// funnel under-counted mid-funnel reach — ApplicationStatsDto). Recorded
/// synchronously inside <see cref="Application.TransitionTo"/> and
/// <see cref="Application.MarkGhosted"/>, so the timeline is atomically consistent
/// with the current status (one UnitOfWork), the same idempotent-side-effect
/// pattern as the AppliedAt stamp. Owned child of the aggregate (referenced by id
/// only; a real status change is never created outside the aggregate — hence the
/// <c>internal</c> factory). Plaintext: status names + timestamps are not PII
/// (ADR 0086 D5 precedent), so no field encryption. Soft-delete parity with
/// <see cref="FollowUp"/>/<see cref="ApplicationNote"/> so an account delete
/// cascades (see <see cref="Application.SoftDelete"/>).
/// </summary>
public sealed class StatusChange : Entity<StatusChangeId>
{
    public ApplicationStatus From { get; private set; } = null!;
    public ApplicationStatus To { get; private set; } = null!;
    public DateTimeOffset ChangedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private StatusChange() { }

    private StatusChange(
        StatusChangeId id,
        ApplicationStatus from,
        ApplicationStatus to,
        DateTimeOffset changedAt) : base(id)
    {
        From = from;
        To = to;
        ChangedAt = changedAt;
    }

    // No validation: the aggregate is the only caller and only ever passes two
    // real ApplicationStatus SmartEnums + the transition's own timestamp, so
    // there is nothing that can fail here (unlike FollowUp.Create, which
    // validates note length). Returns the entity directly rather than a Result
    // (KISS — a never-failing factory does not need the Result wrapper).
    internal static StatusChange Create(
        ApplicationStatus from,
        ApplicationStatus to,
        DateTimeOffset changedAt) =>
        new(StatusChangeId.New(), from, to, changedAt);

    public void SoftDelete(IDateTimeProvider clock) => DeletedAt = clock.UtcNow;
}
