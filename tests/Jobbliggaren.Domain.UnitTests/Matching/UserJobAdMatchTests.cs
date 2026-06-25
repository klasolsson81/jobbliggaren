using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Matching;

/// <summary>
/// ADR 0080 Vag 4 PR-1 — invariants for the <see cref="UserJobAdMatch"/> aggregate root:
/// the Create guards (empty user / default ad / skill cap / dedup), the one-way notification
/// state machine (Pending → Queued → Sent), and idempotent soft-delete. The Goodhart guard
/// (no numeric score on the aggregate) is pinned separately by the architecture fitness
/// function (UserJobAdMatchGoodhartTests).
/// </summary>
public class UserJobAdMatchTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly JobAdId ValidJobAdId = JobAdId.New();

    private static Result<UserJobAdMatch> CreateValid(
        NotifiableMatchGrade grade = NotifiableMatchGrade.Strong,
        IReadOnlyList<string>? skills = null) =>
        UserJobAdMatch.Create(ValidUserId, ValidJobAdId, grade, skills ?? ["csharp", "sql"], Clock);

    // ---------------------------------------------------------------
    // Create — happy path
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_CreatesPendingMatch()
    {
        var result = UserJobAdMatch.Create(
            ValidUserId, ValidJobAdId, NotifiableMatchGrade.Top, ["csharp", "sql"], Clock);

        result.IsSuccess.ShouldBeTrue();
        var match = result.Value;
        match.UserId.ShouldBe(ValidUserId);
        match.JobAdId.ShouldBe(ValidJobAdId);
        match.Grade.ShouldBe(NotifiableMatchGrade.Top);
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        match.MatchedSkillConceptIds.ShouldBe(["csharp", "sql"]);
        match.CreatedAt.ShouldBe(Clock.UtcNow);
        match.SentAt.ShouldBeNull();
        match.DeletedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(NotifiableMatchGrade.Good)]
    [InlineData(NotifiableMatchGrade.Strong)]
    [InlineData(NotifiableMatchGrade.Top)]
    public void Create_WithEachNotifiableGrade_IsAccepted(NotifiableMatchGrade grade)
    {
        // The honest floor (D1): only notifiable grades exist here — Basic is structurally
        // excluded by the NotifiableMatchGrade type, so there is no "below Good" case to test.
        var result = CreateValid(grade: grade);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Grade.ShouldBe(grade);
    }

    // ---------------------------------------------------------------
    // Create — guards
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithEmptyUserId_Fails()
    {
        var result = UserJobAdMatch.Create(
            Guid.Empty, ValidJobAdId, NotifiableMatchGrade.Strong, ["csharp"], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.UserIdRequired");
    }

    [Fact]
    public void Create_WithDefaultJobAdId_Fails()
    {
        var result = UserJobAdMatch.Create(
            ValidUserId, default, NotifiableMatchGrade.Strong, ["csharp"], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.JobAdIdRequired");
    }

    [Fact]
    public void Create_WithMoreThanMaxMatchedSkills_Fails()
    {
        var tooMany = Enumerable.Range(0, UserJobAdMatch.MaxMatchedSkills + 1)
            .Select(i => $"skill-{i}")
            .ToList();

        var result = UserJobAdMatch.Create(
            ValidUserId, ValidJobAdId, NotifiableMatchGrade.Strong, tooMany, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.TooManyMatchedSkills");
    }

    [Fact]
    public void Create_AtExactlyMaxMatchedSkills_IsAccepted()
    {
        // Boundary: the cap is inclusive (> MaxMatchedSkills fails, == passes) after dedup.
        var exactly = Enumerable.Range(0, UserJobAdMatch.MaxMatchedSkills)
            .Select(i => $"skill-{i}")
            .ToList();

        var result = UserJobAdMatch.Create(
            ValidUserId, ValidJobAdId, NotifiableMatchGrade.Strong, exactly, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MatchedSkillConceptIds.Count.ShouldBe(UserJobAdMatch.MaxMatchedSkills);
    }

    [Fact]
    public void Create_WithBlankAndDuplicateSkills_DedupesAndDropsBlanks()
    {
        var result = UserJobAdMatch.Create(
            ValidUserId,
            ValidJobAdId,
            NotifiableMatchGrade.Strong,
            ["csharp", "csharp", "  ", "", "sql", null!],
            Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MatchedSkillConceptIds.ShouldBe(["csharp", "sql"]);
    }

    [Fact]
    public void Create_WithNullSkillsList_TreatsAsEmpty()
    {
        var result = UserJobAdMatch.Create(
            ValidUserId, ValidJobAdId, NotifiableMatchGrade.Good, null!, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MatchedSkillConceptIds.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // State machine — Pending → Queued → Sent (one-way, guarded)
    // ---------------------------------------------------------------

    [Fact]
    public void MarkQueued_FromPending_TransitionsToQueued()
    {
        var match = CreateValid().Value;

        var result = match.MarkQueued();

        result.IsSuccess.ShouldBeTrue();
        match.NotificationStatus.ShouldBe(NotificationStatus.Queued);
    }

    [Fact]
    public void MarkQueued_WhenAlreadyQueued_Fails()
    {
        // Idempotency spine: a re-scan that finds an already-Queued row must not re-queue
        // it (no re-notification). Only a Pending match can be queued.
        var match = CreateValid().Value;
        match.MarkQueued();

        var result = match.MarkQueued();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.NotPending");
        match.NotificationStatus.ShouldBe(NotificationStatus.Queued);
    }

    [Fact]
    public void MarkSent_FromPending_Fails()
    {
        // Cannot send before queuing — the dispatch step claims (Queued) before delivering.
        var match = CreateValid().Value;

        var result = match.MarkSent(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.NotQueued");
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        match.SentAt.ShouldBeNull();
    }

    [Fact]
    public void MarkSent_FromQueued_TransitionsToSentAndStampsSentAt()
    {
        var match = CreateValid().Value;
        match.MarkQueued();
        var sentClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5));

        var result = match.MarkSent(sentClock);

        result.IsSuccess.ShouldBeTrue();
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        match.SentAt.ShouldBe(sentClock.UtcNow);
    }

    [Fact]
    public void MarkSent_WhenAlreadySent_Fails()
    {
        // No re-send: the SentAt stamp is written once. A re-dispatch that finds a Sent row
        // leaves it untouched.
        var match = CreateValid().Value;
        match.MarkQueued();
        var firstSentClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5));
        match.MarkSent(firstSentClock);

        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(30));
        var result = match.MarkSent(laterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.NotQueued");
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        match.SentAt.ShouldBe(firstSentClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // MarkFailed — TD-114 stranded-Queued reaper (Queued → Failed, terminal)
    // ---------------------------------------------------------------

    [Fact]
    public void MarkFailed_FromQueued_TransitionsToFailed()
    {
        var match = CreateValid().Value;
        match.MarkQueued();

        var result = match.MarkFailed();

        result.IsSuccess.ShouldBeTrue();
        match.NotificationStatus.ShouldBe(NotificationStatus.Failed);
        // No timestamp stamped (no new column — aging is by CreatedAt; the reaper logs the reap).
        match.SentAt.ShouldBeNull();
    }

    [Fact]
    public void MarkFailed_FromPending_Fails()
    {
        // A Pending row was never claimed by dispatch — it is not a strand, so it is not reapable.
        var match = CreateValid().Value;

        var result = match.MarkFailed();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.NotQueued");
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public void MarkFailed_WhenAlreadySent_Fails()
    {
        // A Sent row already delivered — never reap a delivered notification.
        var match = CreateValid().Value;
        match.MarkQueued();
        match.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));

        var result = match.MarkFailed();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("UserJobAdMatch.NotQueued");
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public void MarkFailed_IsTerminal_CannotQueueOrSendAfter()
    {
        var match = CreateValid().Value;
        match.MarkQueued();
        match.MarkFailed();

        match.MarkQueued().IsFailure.ShouldBeTrue();
        match.MarkSent(Clock).IsFailure.ShouldBeTrue();
        match.NotificationStatus.ShouldBe(NotificationStatus.Failed);
    }

    // ---------------------------------------------------------------
    // SoftDelete — idempotent
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_WhenActive_StampsDeletedAt()
    {
        var match = CreateValid().Value;
        var deleteClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        match.SoftDelete(deleteClock);

        match.DeletedAt.ShouldBe(deleteClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotentAndDeletedAtUnchanged()
    {
        var match = CreateValid().Value;
        var firstDeleteClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        match.SoftDelete(firstDeleteClock);

        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(5));
        match.SoftDelete(laterClock);

        match.DeletedAt.ShouldBe(firstDeleteClock.UtcNow);
    }
}
