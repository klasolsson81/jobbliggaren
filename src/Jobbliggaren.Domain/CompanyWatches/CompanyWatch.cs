using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// ADR 0087 D3 — a user's follow of an employer, keyed by org.nr. A standalone aggregate
/// root whose intent ("watch this org.nr") is independent of the notification-delivery
/// record that PR-4 (<c>FollowedCompanyAdHit</c>, ADR 0087 D5) will derive from it.
///
/// <para>
/// <b>Identity key: <see cref="UserId"/> (Guid), NOT JobSeekerId</b> — mirrors
/// <c>UserJobAdMatch</c> (ADR 0080 Beslut 1). The notification rail this aggregate feeds is
/// UserId-keyed, so keying here by UserId avoids a <c>JobSeeker.UserId</c> bridge hop on
/// every notification read. Cohesion follows the consumer (ADR 0087 D3).
/// </para>
///
/// <para>
/// <b>Soft-delete + single-row resurrect (FORK B1, senior-cto-advisor 2026-06-30).</b> A
/// follow is consent-adjacent (it feeds notifications) and produces an Art.17 audit posture,
/// so unfollow is a <see cref="SoftDelete"/> (mirrors <c>SavedSearch</c>), and a re-follow of a
/// previously unfollowed org.nr <see cref="Refollow"/>s the SAME row (clears
/// <see cref="DeletedAt"/>, refreshes <see cref="CreatedAt"/>) rather than inserting a second.
/// There is exactly ONE physical row per (UserId, OrganizationNumber) ever — the
/// active-partial <c>UNIQUE(user_id, organization_number) WHERE deleted_at IS NULL</c> guards
/// the active-active race; the resurrect handler avoids accumulating history rows. D3's
/// recoverability requirement is satisfied by the soft-deleted row, not a multi-cycle timeline
/// (YAGNI — no consumer reads per-toggle history).
/// </para>
///
/// <para>
/// <b>company_name is resolved at READ</b> via a projection over <c>job_ads</c> (ADR 0087 D2)
/// — it is a derivable projection, not an invariant, so this aggregate stores NO denormalised
/// snapshot name (it would only go stale).
/// </para>
///
/// <para>
/// <b>No domain events (deliberate, mirrors <c>UserJobAdMatch</c>):</b> the Art.17 cascade is
/// handler-driven by UserId and the PR-4 notification rail is a batch scan — there is no
/// reactive consumer of a follow/unfollow event in v1.
/// </para>
/// </summary>
public sealed class CompanyWatch : AggregateRoot<CompanyWatchId>
{
    public Guid UserId { get; private set; }
    public OrganizationNumber OrganizationNumber { get; private set; } = null!;
    public CompanyWatchTargetType TargetType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private CompanyWatch() { }

    private CompanyWatch(
        CompanyWatchId id,
        Guid userId,
        OrganizationNumber organizationNumber,
        CompanyWatchTargetType targetType,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        OrganizationNumber = organizationNumber;
        TargetType = targetType;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates an active EMPLOYER follow of <paramref name="organizationNumber"/> for
    /// <paramref name="userId"/>. The org.nr is an already-validated <see cref="OrganizationNumber"/>
    /// VO (the caller constructs it via <see cref="OrganizationNumber.Create"/>), so the only
    /// guard here is a non-empty user.
    /// </summary>
    public static Result<CompanyWatch> Follow(
        Guid userId,
        OrganizationNumber organizationNumber,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CompanyWatch>(DomainError.Validation(
                "CompanyWatch.UserIdRequired", "UserId krävs."));

        if (organizationNumber is null)
            return Result.Failure<CompanyWatch>(DomainError.Validation(
                "CompanyWatch.OrganizationNumberRequired", "Organisationsnummer krävs."));

        var watch = new CompanyWatch(
            CompanyWatchId.New(), userId, organizationNumber, CompanyWatchTargetType.Employer, clock.UtcNow);
        return Result.Success(watch);
    }

    /// <summary>
    /// Soft-deletes the follow (unfollow). Idempotent — a no-op on an already-unfollowed watch.
    /// Joins the Art.17 hard-delete cascade by UserId (<c>AccountHardDeleter</c> RemoveRanges
    /// these rows regardless of soft-delete state).
    /// </summary>
    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;
        DeletedAt = clock.UtcNow;
    }

    /// <summary>
    /// Resurrects a previously soft-deleted follow (FORK B1): clears <see cref="DeletedAt"/> and
    /// refreshes <see cref="CreatedAt"/> to now (the follow starts a new active period). Idempotent
    /// — a no-op on an already-active watch, so re-following an active org.nr changes nothing.
    /// </summary>
    public void Refollow(IDateTimeProvider clock)
    {
        if (!DeletedAt.HasValue) return;
        DeletedAt = null;
        CreatedAt = clock.UtcNow;
    }
}
