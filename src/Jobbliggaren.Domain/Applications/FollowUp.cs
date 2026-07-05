using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Applications;

public sealed class FollowUp : Entity<FollowUpId>
{
    public FollowUpChannel Channel { get; private set; } = null!;
    public DateTimeOffset ScheduledAt { get; private set; }
    public string? Note { get; private set; }
    public FollowUpOutcome Outcome { get; private set; } = null!;
    public DateTimeOffset? OutcomeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private FollowUp() { }

    private FollowUp(
        FollowUpId id,
        FollowUpChannel channel,
        DateTimeOffset scheduledAt,
        string? note,
        DateTimeOffset createdAt,
        FollowUpOutcome? outcome = null) : base(id)
    {
        Channel = channel;
        ScheduledAt = scheduledAt;
        Note = note;
        Outcome = outcome ?? FollowUpOutcome.Pending;
        CreatedAt = createdAt;
    }

    internal static Result<FollowUp> Create(
        FollowUpChannel channel,
        DateTimeOffset scheduledAt,
        string? note,
        IDateTimeProvider clock)
    {
        if (note is not null && note.Length > 2000)
            return Result.Failure<FollowUp>(
                DomainError.Validation("FollowUp.NoteTooLong", "Anteckning får vara max 2 000 tecken."));

        return Result.Success(
            new FollowUp(FollowUpId.New(), channel, scheduledAt, note?.Trim(), clock.UtcNow));
    }

    /// <summary>
    /// A completed contact logged today (ADR 0092 D4/D5 "Logga uppföljning"):
    /// channel defaults to <see cref="FollowUpChannel.Other"/>, scheduled = now, and
    /// the outcome starts as <see cref="FollowUpOutcome.Logged"/> (never Pending, so
    /// it does not fire the overdue-follow-up signal). Distinct from
    /// <see cref="Create"/>, which schedules a future reminder.
    /// </summary>
    internal static Result<FollowUp> CreateLogged(string? note, IDateTimeProvider clock)
    {
        if (note is not null && note.Length > 2000)
            return Result.Failure<FollowUp>(
                DomainError.Validation("FollowUp.NoteTooLong", "Anteckning får vara max 2 000 tecken."));

        var now = clock.UtcNow;
        return Result.Success(
            new FollowUp(FollowUpId.New(), FollowUpChannel.Other, now, note?.Trim(), now, FollowUpOutcome.Logged));
    }

    public Result RecordOutcome(FollowUpOutcome outcome, IDateTimeProvider clock)
    {
        if (Outcome != FollowUpOutcome.Pending)
            return Result.Failure(
                DomainError.Conflict("FollowUp.OutcomeAlreadyRecorded", "Utfall har redan registrerats."));

        Outcome = outcome;
        OutcomeAt = clock.UtcNow;
        return Result.Success();
    }

    public void SoftDelete(IDateTimeProvider clock) => DeletedAt = clock.UtcNow;
}
