using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers.Events;

namespace JobbPilot.Domain.JobSeekers;

public sealed class JobSeeker : AggregateRoot<JobSeekerId>
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public Preferences Preferences { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private JobSeeker() { }

    private JobSeeker(
        JobSeekerId id,
        Guid userId,
        string displayName,
        Preferences preferences,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        DisplayName = displayName;
        Preferences = preferences;
        CreatedAt = createdAt;
    }

    public static Result<JobSeeker> Register(
        Guid userId,
        string? displayName,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.UserIdRequired", "UserId krävs."));

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.DisplayNameRequired", "Visningsnamn är obligatoriskt."));

        if (displayName.Length > 200)
            return Result.Failure<JobSeeker>(
                DomainError.Validation("JobSeeker.DisplayNameTooLong", "Visningsnamn får vara max 200 tecken."));

        var now = clock.UtcNow;
        var id = JobSeekerId.New();
        var jobSeeker = new JobSeeker(id, userId, displayName.Trim(), new Preferences(), now);
        jobSeeker.RaiseDomainEvent(
            new JobSeekerRegisteredDomainEvent(id, userId, displayName.Trim(), now));

        return Result.Success(jobSeeker);
    }

    public Result UpdateDisplayName(string? displayName, IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure(
                DomainError.Validation("JobSeeker.DisplayNameRequired", "Visningsnamn är obligatoriskt."));

        if (displayName.Length > 200)
            return Result.Failure(
                DomainError.Validation("JobSeeker.DisplayNameTooLong", "Visningsnamn får vara max 200 tecken."));

        DisplayName = displayName.Trim();
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    public void UpdatePreferences(Preferences preferences, IDateTimeProvider clock)
    {
        Preferences = preferences;
        UpdatedAt = clock.UtcNow;
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        RaiseDomainEvent(new JobSeekerDeletedDomainEvent(Id, clock.UtcNow));
    }
}
