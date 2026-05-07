using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Applications;

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
        DateTimeOffset createdAt) : base(id)
    {
        Channel = channel;
        ScheduledAt = scheduledAt;
        Note = note;
        Outcome = FollowUpOutcome.Pending;
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
