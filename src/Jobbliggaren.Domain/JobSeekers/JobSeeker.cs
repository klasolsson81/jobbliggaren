using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers.Events;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Domain.JobSeekers;

public sealed class JobSeeker : AggregateRoot<JobSeekerId>
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public Preferences Preferences { get; private set; } = null!;

    // F4-12 (ADR 0076) — the user's STATED job-search preferences (desired
    // occupation-groups/regions/employment-types) that feed the deterministic
    // match score. Distinct concern from notification/locale Preferences (SRP).
    // Defaults to Empty so a freshly-registered seeker has a valid (empty) value.
    public MatchPreferences MatchPreferences { get; private set; } = MatchPreferences.Empty;

    public ResumeId? PrimaryResumeId { get; private set; }
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

    /// <summary>
    /// Sets the job-seeker's STATED job-search preferences (F4-12, ADR 0076).
    /// Mirrors <see cref="UpdatePreferences"/>: replaces the value object + bumps
    /// <see cref="UpdatedAt"/>. Raises NO domain event — there is no reactive
    /// consumer (matching is compute-on-demand; CTO-bound). An empty
    /// <see cref="MatchPreferences"/> is valid (clears stated preferences).
    /// </summary>
    public void UpdateMatchPreferences(MatchPreferences matchPreferences, IDateTimeProvider clock)
    {
        MatchPreferences = matchPreferences;
        UpdatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Sätter primary Resume för denna JobSeeker. Atomic swap — invarianten
    /// "exakt 1 primary per JobSeeker" hålls trivialt eftersom state ägs här
    /// (ADR 0058 + senior-cto-advisor 2026-05-20 Alt A2). Handler ansvarar
    /// för cross-aggregat-validering att <paramref name="resumeId"/> tillhör
    /// denna JobSeeker — Resume har ingen knowledge om sin egen primary-status.
    /// Idempotent vid samma ID.
    /// </summary>
    public Result SetPrimaryResume(ResumeId resumeId, IDateTimeProvider clock)
    {
        if (resumeId == default)
            return Result.Failure(DomainError.Validation(
                "JobSeeker.PrimaryResumeIdRequired", "Resume-id krävs."));

        if (PrimaryResumeId == resumeId)
            return Result.Success();

        PrimaryResumeId = resumeId;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new PrimaryResumeSetDomainEvent(Id, resumeId, clock.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Nullar primary Resume. Anropas av <c>DeleteResumeCommandHandler</c> som
    /// del av cascade när den primary-markerade Resume soft-raderas. Idempotent.
    /// </summary>
    public Result UnsetPrimaryResume(IDateTimeProvider clock)
    {
        if (PrimaryResumeId is null)
            return Result.Success();

        PrimaryResumeId = null;
        UpdatedAt = clock.UtcNow;
        RaiseDomainEvent(new PrimaryResumeSetDomainEvent(Id, null, clock.UtcNow));
        return Result.Success();
    }

    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;

        DeletedAt = clock.UtcNow;
        RaiseDomainEvent(new JobSeekerDeletedDomainEvent(Id, clock.UtcNow));
    }
}
