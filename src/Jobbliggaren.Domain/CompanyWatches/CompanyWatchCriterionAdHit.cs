using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// ADR 0175 D5 (#1663) — one new Active ad from a company a user's Branschbevakning
/// (<see cref="CompanyWatchCriterion"/>) matches. Written by the nightly scan, one row per
/// (criterion, ad); the UNIQUE key on that pair is the scan's idempotency backstop.
///
/// <para>
/// <b>A sibling of <see cref="FollowedCompanyAdHit"/>, not a reuse of its table</b> (ADR 0175 D5): the follow
/// digest's pending set and the follow source's marks have no watch join, so criterion rows there would be
/// mailed and marked as follow hits.
/// </para>
///
/// <para>
/// <b>The row is an ungraded fact under Art. 6(1)(b)</b> (ADR 0175 D4). It carries no organisation number, no
/// grade, no score, no mail state and no <c>SeenAt</c>; whether an ad matches the user is computed at read
/// time. <see cref="JobAdId"/> is held by identity (no FK); the criterion is a real FK with
/// <c>ON DELETE CASCADE</c>, modelled in EF so the Art. 17 delete orders the hits before their criterion.
/// </para>
///
/// <para>
/// <b>The bell's two flags have no writer yet.</b> They are created false and are written only by the user's
/// own click in the bell (PR 3); a system writer of either is Blocker class (ADR 0169 V20).
/// </para>
/// </summary>
public sealed class CompanyWatchCriterionAdHit : AggregateRoot<CompanyWatchCriterionAdHitId>
{
    public Guid UserId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public CompanyWatchCriterionId CompanyWatchCriterionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The bell's read flag (ADR 0168 parity). Written only by the user's click in the bell (PR 3).</summary>
    public bool ReadInNotificationFeed { get; private set; }

    /// <summary>The bell's X (ADR 0169 parity); implies <see cref="ReadInNotificationFeed"/> by a CHECK.</summary>
    public bool DismissedFromNotificationFeed { get; private set; }

    // EF Core constructor
    private CompanyWatchCriterionAdHit() { }

    private CompanyWatchCriterionAdHit(
        CompanyWatchCriterionAdHitId id,
        Guid userId,
        JobAdId jobAdId,
        CompanyWatchCriterionId criterionId,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        JobAdId = jobAdId;
        CompanyWatchCriterionId = criterionId;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// A hit for <paramref name="jobAdId"/> under <paramref name="criterion"/>. The owner and the criterion id
    /// are taken from the criterion, never passed in (ADR 0175 D2): the denormalised <see cref="UserId"/> has
    /// no database constraint tying it to the criterion's owner, so the aggregate is where that holds.
    /// <see cref="CreatedAt"/> is read from the clock on every call, never a run-wide instant: a burst stamped
    /// with one value would turn the acknowledgement window's whole-window tie into a live state.
    /// </summary>
    public static Result<CompanyWatchCriterionAdHit> Create(
        CompanyWatchCriterion criterion,
        JobAdId jobAdId,
        IDateTimeProvider clock)
    {
        if (criterion is null)
            return Result.Failure<CompanyWatchCriterionAdHit>(DomainError.Validation(
                "CompanyWatchCriterionAdHit.CriterionRequired", "Branschbevakning krävs."));

        if (jobAdId == default)
            return Result.Failure<CompanyWatchCriterionAdHit>(DomainError.Validation(
                "CompanyWatchCriterionAdHit.JobAdIdRequired", "JobAd-id krävs."));

        var hit = new CompanyWatchCriterionAdHit(
            CompanyWatchCriterionAdHitId.New(), criterion.UserId, jobAdId, criterion.Id, clock.UtcNow);
        return Result.Success(hit);
    }
}
