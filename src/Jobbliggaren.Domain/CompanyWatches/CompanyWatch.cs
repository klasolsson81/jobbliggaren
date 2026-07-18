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

    /// <summary>
    /// Per-watch notification filter (RF-2, 2026-07-12) — null = no filter (the canonical
    /// "show all" representation; a present spec always narrows something). Persisted as a
    /// nullable jsonb column via a property-level converter (Infrastructure).
    /// </summary>
    public WatchFilterSpec? Filter { get; private set; }

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
    ///
    /// <para>
    /// <b>Clears the per-watch <see cref="Filter"/> (RF-2 sub-bind, senior-cto-advisor
    /// 2026-07-12):</b> unfollow ends the 6(1)(b) relation, so its setting has no remaining
    /// processing purpose — profiling-adjacent preference data must not sit latent on the
    /// soft-deleted row (Art. 5(1)(c)/(e)), and a later <see cref="Refollow"/> must start as a
    /// clean show-all follow (§5 transparency — no silently-inherited narrowing).
    /// </para>
    /// </summary>
    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;
        DeletedAt = clock.UtcNow;
        Filter = null;
    }

    /// <summary>
    /// Resurrects a previously soft-deleted follow (FORK B1): clears <see cref="DeletedAt"/> and
    /// refreshes <see cref="CreatedAt"/> to now (the follow starts a new active period). Idempotent
    /// — a no-op on an already-active watch, so re-following an active org.nr changes nothing.
    /// The resurrected follow has no <see cref="Filter"/> — <see cref="SoftDelete"/> already
    /// cleared it (RF-2 sub-bind), so no filter-specific logic is needed here.
    /// </summary>
    public void Refollow(IDateTimeProvider clock)
    {
        if (!DeletedAt.HasValue) return;
        DeletedAt = null;
        CreatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Sets (or replaces) the per-watch notification filter (RF-2, 2026-07-12). The spec is an
    /// already-validated <see cref="WatchFilterSpec"/> (the caller constructs it via
    /// <see cref="WatchFilterSpec.Create"/>). Precondition: the watch is active — filtering an
    /// unfollowed watch is meaningless (its filter was cleared by <see cref="SoftDelete"/>).
    /// </summary>
    public Result SetFilter(WatchFilterSpec filter)
    {
        if (filter is null)
            return Result.Failure(DomainError.Validation(
                "CompanyWatch.FilterRequired",
                "Ett filter krävs — använd ClearFilter för att ta bort filtret."));

        if (DeletedAt.HasValue)
            return Result.Failure(DomainError.Validation(
                "CompanyWatch.NotActive",
                "Bevakningen är borttagen och kan inte filtreras."));

        Filter = filter;
        return Result.Success();
    }

    /// <summary>
    /// Removes the per-watch filter (back to show-all). Idempotent — a no-op when no filter
    /// is set; allowed regardless of soft-delete state (clearing never widens exposure).
    /// </summary>
    public void ClearFilter() => Filter = null;

    /// <summary>
    /// #544 backfill (ADR 0090 D5) — replace a stored PLAINTEXT personnummer-shaped org.nr with its
    /// HMAC token, in place, discarding the plaintext (irreversible — the point). The token is
    /// computed by the Application layer (the Domain never sees the pepper) and handed in as an
    /// already-wrapped VO. Returns <see langword="true"/> when it converted.
    /// <para>
    /// <b>Idempotent by shape (the SSOT discriminator):</b> a no-op when the current value is not a
    /// 10-digit personnummer-shaped plaintext — i.e. a legal-entity (AB) org.nr (stays plaintext) or
    /// an already-tokenised value (a 64-char token → <see cref="OrganizationNumber.Value"/> length ≠
    /// 10). So a re-run, or the run after a crash, never double-tokenises.
    /// </para>
    /// </summary>
    public bool ApplyOrganizationNumberTokenBackfill(OrganizationNumber tokenized)
    {
        if (tokenized is null)
            return false;
        // Only a 10-digit personnummer-shaped PLAINTEXT value is convertible. A token has length ≠ 10
        // (IsPersonnummerShaped true via the fail-safe), so it is excluded here; an AB org.nr is a
        // 10-digit value that is NOT personnummer-shaped, so it stays plaintext.
        if (OrganizationNumber.Value.Length != 10 || !OrganizationNumber.IsPersonnummerShaped())
            return false;

        OrganizationNumber = tokenized;
        return true;
    }
}
