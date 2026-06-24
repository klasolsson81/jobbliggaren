using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Domain.Matching;

/// <summary>
/// ADR 0080 Vag 4 — a persisted background-match between a user and a job ad. A standalone
/// aggregate root (independent lifecycle + its own notification state machine), keyed by
/// <c>(UserId, JobAdId)</c> UNIQUE — the dedup spine that lets the nightly Worker scan be
/// idempotent (a re-run never re-notifies, because the row already exists in a non-Pending
/// status). References the <see cref="JobAds.JobAdId"/> by identity, never an FK (ADR
/// 0058/0059 soft-delete isolation — the join is handler-managed).
/// <para>
/// <b>Goodhart (D7, ADR 0071/0076):</b> the aggregate persists the <see cref="Grade"/>
/// NAMED CATEGORY (so digest routing can split Top-vs-Strong without recompute) and NEVER
/// a numeric score — an architecture test forbids any numeric field. Digest ordering uses
/// the grade enum + <see cref="CreatedAt"/>, never a stored magnitude.
/// </para>
/// <para>
/// <b>Honest floor (D1):</b> only notifiable grades exist here — the type
/// <see cref="NotifiableMatchGrade"/> structurally excludes Basic / "no grade" (the Worker
/// maps the computed grade and simply does not persist a match below Good). The
/// <see cref="MatchedSkillConceptIds"/> are plaintext (DEK-free) explainability evidence
/// (ADR 0071 "explainable by design") carried into the in-app surface / future email.
/// </para>
/// </summary>
public sealed class UserJobAdMatch : AggregateRoot<UserJobAdMatchId>
{
    // Defense against jsonb bloat — the matched-skill evidence is the intersection of the
    // ad's requirements and the user's confirmed skills, naturally a handful; cap generously.
    public const int MaxMatchedSkills = 50;

    private readonly List<string> _matchedSkillConceptIds = [];

    public Guid UserId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public NotifiableMatchGrade Grade { get; private set; }
    public NotificationStatus NotificationStatus { get; private set; }

    /// <summary>Plaintext concept-ids (DEK-free) — the matched-skill evidence for the surface.</summary>
    public IReadOnlyList<string> MatchedSkillConceptIds => _matchedSkillConceptIds.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private UserJobAdMatch() { }

    private UserJobAdMatch(
        UserJobAdMatchId id,
        Guid userId,
        JobAdId jobAdId,
        NotifiableMatchGrade grade,
        IEnumerable<string> matchedSkillConceptIds,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        JobAdId = jobAdId;
        Grade = grade;
        NotificationStatus = NotificationStatus.Pending;
        _matchedSkillConceptIds.AddRange(matchedSkillConceptIds);
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a Pending match. <paramref name="grade"/> is a <see cref="NotifiableMatchGrade"/>
    /// by type, so Basic / no-grade is excluded at the source (the honest floor — D1). The
    /// matched skills are capped (<see cref="MaxMatchedSkills"/>) and deduped.
    /// </summary>
    public static Result<UserJobAdMatch> Create(
        Guid userId,
        JobAdId jobAdId,
        NotifiableMatchGrade grade,
        IReadOnlyList<string> matchedSkillConceptIds,
        IDateTimeProvider clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<UserJobAdMatch>(
                DomainError.Validation("UserJobAdMatch.UserIdRequired", "UserId krävs."));

        if (jobAdId == default)
            return Result.Failure<UserJobAdMatch>(
                DomainError.Validation("UserJobAdMatch.JobAdIdRequired", "JobAd-id krävs."));

        var skills = (matchedSkillConceptIds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (skills.Count > MaxMatchedSkills)
            return Result.Failure<UserJobAdMatch>(DomainError.Validation(
                "UserJobAdMatch.TooManyMatchedSkills",
                $"Max {MaxMatchedSkills} matchade kompetenser per match."));

        var match = new UserJobAdMatch(
            UserJobAdMatchId.New(), userId, jobAdId, grade, skills, clock.UtcNow);
        return Result.Success(match);
    }

    /// <summary>
    /// Pending → Queued (the dispatch step claimed this match). Idempotency guard: only a
    /// Pending match can be queued — a re-scan that finds an already-Queued/Sent row leaves
    /// it untouched (no re-notification).
    /// </summary>
    public Result MarkQueued()
    {
        if (NotificationStatus != NotificationStatus.Pending)
            return Result.Failure(DomainError.Validation(
                "UserJobAdMatch.NotPending",
                "Endast en väntande match kan köas för notifiering."));

        NotificationStatus = NotificationStatus.Queued;
        return Result.Success();
    }

    /// <summary>
    /// Queued → Sent (the notification was delivered). Cannot send before queuing and cannot
    /// re-send (the SentAt stamp is written once).
    /// </summary>
    public Result MarkSent(IDateTimeProvider clock)
    {
        if (NotificationStatus != NotificationStatus.Queued)
            return Result.Failure(DomainError.Validation(
                "UserJobAdMatch.NotQueued",
                "Endast en köad match kan markeras som skickad."));

        NotificationStatus = NotificationStatus.Sent;
        SentAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Soft-deletes the match. Joins the Art.17 hard-delete cascade by UserId
    /// (<c>AccountHardDeleter</c> RemoveRanges these rows), and the handler-managed cascade
    /// when the JobAd expires. Idempotent.
    /// <para>
    /// <b>No domain events on this aggregate (deliberate):</b> background matching is a
    /// batch concern (ADR 0080 Beslut 2 rejects per-match events on YAGNI/audit-aggregation
    /// grounds) and the Art.17 cascade is handler-driven by UserId — there is no reactive
    /// consumer, so neither <see cref="SoftDelete"/> nor the state transitions raise an event.
    /// </para>
    /// </summary>
    public void SoftDelete(IDateTimeProvider clock)
    {
        if (DeletedAt.HasValue) return;
        DeletedAt = clock.UtcNow;
    }
}
