using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — a persisted notification-delivery record for a new job ad from an
/// employer a user follows. The notification spine for company follows, keyed by
/// <c>(UserId, JobAdId, CompanyWatchId)</c> UNIQUE — the dedup spine that lets the nightly
/// <c>CompanyWatchScanJob</c> be idempotent (a re-run never re-notifies, because the row already
/// exists in a non-Pending status). References <see cref="JobAds.JobAdId"/> and
/// <see cref="CompanyWatches.CompanyWatchId"/> by identity, never an FK (ADR 0058/0059 soft-delete
/// isolation — a retracted ad or an unfollowed watch must not delete a delivery row; the join is
/// handler-managed).
/// <para>
/// <b>NOT a reuse of <c>UserJobAdMatch</c> (ADR 0087 D5, Alt X — rejected):</b> a company-follow hit
/// has NO skill grade — it is not scored by a matching engine. Riding <c>UserJobAdMatch</c> would
/// require a synthetic 4th grade (breaks the <c>UserJobAdMatchGoodhartTests</c> pin) or a source
/// discriminator (makes that aggregate's invariant false). Both corrupt a deliberately sealed
/// aggregate to save a table (SRP, Martin ch. 7). This aggregate therefore mirrors the state-machine
/// SHAPE (<see cref="FollowedCompanyAdHitStatus"/> Pending → Queued → Sent) but carries NO grade and
/// no numeric score — no new Goodhart surface (ADR 0071/0076).
/// </para>
/// <para>
/// <b>Identity key: <see cref="UserId"/> (Guid), NOT JobSeekerId</b> — mirrors <c>UserJobAdMatch</c>
/// / <c>CompanyWatch</c> (ADR 0080 Beslut 1 / ADR 0087 D3). The rail is UserId-keyed, so keying here
/// by UserId avoids a <c>JobSeeker.UserId</c> bridge hop on every notification read.
/// </para>
/// <para>
/// <b>No domain events (deliberate, mirrors <c>UserJobAdMatch</c>):</b> the company-follow scan is a
/// batch concern and the Art. 17 cascade is handler-driven by UserId (<c>AccountHardDeleter</c>
/// hard-deletes these rows) — there is no reactive consumer of a hit event in v1.
/// </para>
/// </summary>
public sealed class FollowedCompanyAdHit : AggregateRoot<FollowedCompanyAdHitId>
{
    public Guid UserId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public CompanyWatchId CompanyWatchId { get; private set; }
    public FollowedCompanyAdHitStatus NotificationStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>
    /// #453 (cross-channel dedup) — when the user opened this ad in-app (any surface: the ad-detail
    /// page/modal, reached via search/match/saved/shared link). While a hit is still
    /// <see cref="FollowedCompanyAdHitStatus.Pending"/>, a non-null <see cref="SeenAt"/> SUPPRESSES the
    /// follow-notification email (the digest due-set filters <c>Pending AND SeenAt == null</c>) — the
    /// invariant "aldrig mejla något jag sett i appen". Null = not yet seen in-app. See
    /// <see cref="MarkSeen"/> for the Pending-only transition.
    /// </summary>
    public DateTimeOffset? SeenAt { get; private set; }

    // EF Core constructor
    private FollowedCompanyAdHit() { }

    private FollowedCompanyAdHit(
        FollowedCompanyAdHitId id,
        Guid userId,
        JobAdId jobAdId,
        CompanyWatchId companyWatchId,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        JobAdId = jobAdId;
        CompanyWatchId = companyWatchId;
        NotificationStatus = FollowedCompanyAdHitStatus.Pending;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a Pending hit linking <paramref name="userId"/> to the new <paramref name="jobAdId"/>
    /// that matched their <paramref name="companyWatchId"/> follow. Guards non-empty user + non-default
    /// ids (the scan constructs these from its own read of active followers' watches — 7C, no consent
    /// gate on hit creation — so the guards are defense-in-depth against a caller bug).
    /// </summary>
    public static Result<FollowedCompanyAdHit> Create(
        Guid userId,
        JobAdId jobAdId,
        CompanyWatchId companyWatchId,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<FollowedCompanyAdHit>(DomainError.Validation(
                "FollowedCompanyAdHit.UserIdRequired", "UserId krävs."));

        if (jobAdId == default)
            return Result.Failure<FollowedCompanyAdHit>(DomainError.Validation(
                "FollowedCompanyAdHit.JobAdIdRequired", "JobAd-id krävs."));

        if (companyWatchId == default)
            return Result.Failure<FollowedCompanyAdHit>(DomainError.Validation(
                "FollowedCompanyAdHit.CompanyWatchIdRequired", "CompanyWatch-id krävs."));

        var hit = new FollowedCompanyAdHit(
            FollowedCompanyAdHitId.New(), userId, jobAdId, companyWatchId, clock.UtcNow);
        return Result.Success(hit);
    }

    /// <summary>
    /// Pending → Queued (the dispatch step claimed this hit). Idempotency guard: only a Pending hit
    /// can be queued — a re-scan/re-dispatch that finds an already-Queued/Sent row leaves it
    /// untouched (no re-notification).
    /// </summary>
    public Result MarkQueued()
    {
        if (NotificationStatus != FollowedCompanyAdHitStatus.Pending)
            return Result.Failure(DomainError.Validation(
                "FollowedCompanyAdHit.NotPending",
                "Endast en väntande företagsträff kan köas för notifiering."));

        NotificationStatus = FollowedCompanyAdHitStatus.Queued;
        return Result.Success();
    }

    /// <summary>
    /// Queued → Sent (the notification was delivered). Cannot send before queuing and cannot re-send
    /// (the <see cref="SentAt"/> stamp is written once).
    /// </summary>
    public Result MarkSent(IDateTimeProvider clock)
    {
        if (NotificationStatus != FollowedCompanyAdHitStatus.Queued)
            return Result.Failure(DomainError.Validation(
                "FollowedCompanyAdHit.NotQueued",
                "Endast en köad företagsträff kan markeras som skickad."));

        NotificationStatus = FollowedCompanyAdHitStatus.Sent;
        SentAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// #453 (cross-channel dedup) — stamps <see cref="SeenAt"/> because the user opened this ad in-app.
    /// The follow-notification digest suppresses any hit with a non-null <see cref="SeenAt"/> ("aldrig
    /// mejla något jag sett i appen").
    /// <para>
    /// <b>Pending-only, by design (senior-cto-advisor 2026-07-02, Q2):</b> only a
    /// <see cref="FollowedCompanyAdHitStatus.Pending"/> hit can be marked seen. After the dispatch has
    /// claimed the hit (<see cref="FollowedCompanyAdHitStatus.Queued"/>/<see cref="FollowedCompanyAdHitStatus.Sent"/>)
    /// the email decision is already taken, so a post-claim MarkSeen is a deliberate no-op — the
    /// Sent-dedup already prevents repetition. Idempotent: a re-open leaves the first-seen stamp intact
    /// (the invariant is only <c>SeenAt != null</c>). A stamped-but-Pending hit falls dormant (the
    /// digest never claims it, so it is never a strand candidate; the scan's
    /// <c>UNIQUE(UserId, JobAdId, CompanyWatchId)</c> triple-dedup never re-creates it).
    /// </para>
    /// </summary>
    public Result MarkSeen(IDateTimeProvider clock)
    {
        // Post-claim (Queued/Sent): the email decision is already made — deliberate no-op (not a failure).
        if (NotificationStatus != FollowedCompanyAdHitStatus.Pending)
            return Result.Success();

        // Idempotent — first-seen wins (the invariant is only that SeenAt is non-null once seen).
        if (SeenAt.HasValue)
            return Result.Success();

        SeenAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// #453 (audit #26) — Queued → Failed (terminal). The <c>StrandedMatchReaperJob</c> follow arm calls
    /// this on a row that sat <see cref="FollowedCompanyAdHitStatus.Queued"/> past the strand threshold
    /// (a send that never completed). Guarded: only a Queued hit can fail — a Pending row was never
    /// claimed and a Sent row already delivered, so neither is reapable. NEVER re-sends (the "never
    /// double-email" stance) and stamps no timestamp (aging is by <see cref="CreatedAt"/>, the reaper
    /// logs the reap). Terminal: there is no transition out of Failed. Parity
    /// <c>UserJobAdMatch.MarkFailed</c>.
    /// </summary>
    public Result MarkFailed()
    {
        if (NotificationStatus != FollowedCompanyAdHitStatus.Queued)
            return Result.Failure(DomainError.Validation(
                "FollowedCompanyAdHit.NotQueued",
                "Endast en köad företagsträff kan markeras som misslyckad."));

        NotificationStatus = FollowedCompanyAdHitStatus.Failed;
        return Result.Success();
    }
}
