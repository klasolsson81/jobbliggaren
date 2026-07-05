using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Attention;

/// <summary>
/// Pure unit tests for the /ansokningar attention-prioritisation rule (ADR 0085
/// §3). No DbContext, no clock — <c>now</c> is injected, so the whole policy is
/// exercised in-memory (CLAUDE.md §2.4).
/// </summary>
public class ApplicationAttentionEvaluatorTests
{
    // Fixed anchor — all relative ages are computed from this instant.
    private static readonly DateTimeOffset Now =
        new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly ApplicationAttentionOptions Options = new(); // 7 / 5

    // ---- Signal 1: Offer awaiting reply ----

    [Fact]
    public void Evaluate_OfferReceived_ReturnsOfferAwaitingReply()
    {
        var dto = Dto(ApplicationStatus.OfferReceived, lastStatusChangeAt: Now);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OfferAwaitingReply);
    }

    [Fact]
    public void Evaluate_OfferReceivedAndOverdueFollowUp_OfferWinsByPriority()
    {
        // Priority order (ADR 0085 §3): offer (1) before overdue follow-up (2).
        var dto = Dto(ApplicationStatus.OfferReceived, lastStatusChangeAt: Now, hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OfferAwaitingReply);
    }

    // ---- Signal 2: Overdue scheduled follow-up ----

    [Fact]
    public void Evaluate_SubmittedWithOverdueFollowUp_ReturnsOverdueFollowUp()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now,
            appliedAt: Now, hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    [Fact]
    public void Evaluate_DraftWithOverdueFollowUpAndApproachingDeadline_OverdueWinsByPriority()
    {
        // A draft can hold a follow-up (AddFollowUp is blocked only for closed
        // applications). Priority order: overdue follow-up (2) before draft deadline (5).
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now,
            hasOverdueFollowUp: true, expiresAt: Now.AddDays(2));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    // ---- Signal 5: Draft with approaching deadline (+ the draft-only invariant) ----

    [Fact]
    public void Evaluate_DraftWithDeadlineWithinThreshold_ReturnsDraftDeadlineApproaching()
    {
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: Now.AddDays(3));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.DraftDeadlineApproaching);
    }

    [Fact]
    public void Evaluate_DraftWithPassedDeadline_ReturnsNone()
    {
        // Deadline already passed → not actionable as a draft-deadline nudge.
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: Now.AddDays(-1));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Fact]
    public void Evaluate_DraftWithNoJobAd_ReturnsNone()
    {
        // No job ad → no deadline to approach (manual drafts without an ExpiresAt).
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: null);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    /// <summary>
    /// THE BINDING INVARIANT (ADR 0085 §3, Klas 2026-06-28): the application
    /// deadline is attention-relevant ONLY for drafts. A submitted application
    /// whose deadline is approaching must NEVER surface a draft-deadline signal —
    /// once submitted the deadline is moot. This test pins it against regression.
    /// </summary>
    [Theory]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    [InlineData("InterviewScheduled")]
    [InlineData("Interviewing")]
    [InlineData("OfferReceived")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Accepted")]
    [InlineData("Ghosted")]
    public void Evaluate_NonDraftWithApproachingDeadline_NeverReturnsDraftDeadline(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        // Deadline within the draft window, but the application is not a draft.
        // Keep the other signals quiet: recent status change, no apply nudge, no overdue follow-up.
        var dto = Dto(status, lastStatusChangeAt: Now, appliedAt: Now, expiresAt: Now.AddDays(1));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldNotBe(ApplicationAttentionSignal.DraftDeadlineApproaching);
    }

    // ---- Signal 4: No response for long ----

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    public void Evaluate_AwaitingEmployerBeyondGhostedThreshold_ReturnsNoResponseLong(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        // 21 days since last status change, applied recently so the proactive nudge is quiet.
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-21), appliedAt: Now, ghostedThresholdDays: 21);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseLong);
    }

    [Fact]
    public void Evaluate_NoResponseAndProactiveBothDue_NoResponseWinsByPriority()
    {
        // Both signal 4 (no response, priority) and signal 3 (proactive) fire.
        // Priority order: no response (4) before proactive nudge (3).
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-30), appliedAt: Now.AddDays(-30), ghostedThresholdDays: 21);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseLong);
    }

    [Fact]
    public void Evaluate_SubmittedWithApproachingDeadlineAndProactiveDue_ReturnsProactiveNotDraftDeadline()
    {
        // Reinforces the draft-only invariant under priority interaction: a
        // submitted application whose deadline is within the draft window must
        // NOT take the draft-deadline branch — it falls through to the proactive
        // nudge. Guards against a refactor that moved the deadline check above the
        // status guard while another signal masks the regression.
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now,
            appliedAt: Now.AddDays(-7), expiresAt: Now.AddDays(1));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.ProactiveFollowUpNudge);
    }

    // ---- Signal 3: Proactive follow-up nudge ----

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    public void Evaluate_AwaitingEmployerBeyondNudgeDays_ReturnsProactiveFollowUpNudge(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        // Applied 7 days ago (nudge), but last status changed recently (no-response quiet).
        var dto = Dto(status, lastStatusChangeAt: Now, appliedAt: Now.AddDays(-7), ghostedThresholdDays: 21);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.ProactiveFollowUpNudge);
    }

    [Fact]
    public void Evaluate_SubmittedWithoutAppliedAt_DoesNotReturnProactiveNudge()
    {
        // Defensive: a submitted application always has an AppliedAt, but a null
        // anchor must not crash or falsely nudge.
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now, appliedAt: null);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    // ---- None ----

    [Fact]
    public void Evaluate_RecentSubmittedWithNoSignals_ReturnsNone()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now, appliedAt: Now);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Accepted")]
    [InlineData("Ghosted")]
    public void Evaluate_TerminalOrGhostedStatus_ReturnsNone(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        // Old status change + old apply date: neither no-response nor proactive
        // applies to non-awaiting statuses.
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-60), appliedAt: Now.AddDays(-60));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Theory]
    [InlineData("InterviewScheduled")]
    [InlineData("Interviewing")]
    public void Evaluate_ActiveButNotAwaitingEmployer_ReturnsNone_EvenWhenNudgeAndNoResponseWouldFire(
        string statusName)
    {
        // Pins the IsAwaitingEmployer gate: the no-response (4) and proactive (3)
        // signals fire ONLY for Submitted/Acknowledged. Interview states are
        // active but not "awaiting an employer reply", so even with both
        // thresholds far exceeded they must stay None — guards against a
        // well-meaning broadening of IsAwaitingEmployer.
        var status = ApplicationStatus.FromName(statusName);
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-60), appliedAt: Now.AddDays(-60));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    // ---- Boundary cases (M/G/N: below / at / above each threshold) ----

    [Theory]
    [InlineData(6, false)] // below: 6 days < 7-day nudge
    [InlineData(7, true)]  // at: ≥ fires
    [InlineData(8, true)]  // above
    public void Evaluate_ProactiveNudgeBoundary_FiresAtOrAboveThreshold(int daysSinceApplied, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now, appliedAt: Now.AddDays(-daysSinceApplied), ghostedThresholdDays: 21);

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.ProactiveFollowUpNudge;

        fired.ShouldBe(shouldFire);
    }

    [Theory]
    [InlineData(20, false)] // below: 20 days < 21-day ghosted threshold
    [InlineData(21, true)]  // at: ≥ fires
    [InlineData(22, true)]  // above
    public void Evaluate_NoResponseBoundary_FiresAtOrAboveThreshold(int daysSinceStatusChange, bool shouldFire)
    {
        // Apply date recent so only the no-response signal is in play.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-daysSinceStatusChange), appliedAt: Now, ghostedThresholdDays: 21);

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.NoResponseLong;

        fired.ShouldBe(shouldFire);
    }

    [Theory]
    [InlineData(4, true)]   // within: 4 days ≤ 5-day window
    [InlineData(5, true)]   // at: ≤ fires
    [InlineData(6, false)]  // beyond: 6 days > 5-day window
    public void Evaluate_DraftDeadlineBoundary_FiresAtOrWithinThreshold(int daysUntilDeadline, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: Now.AddDays(daysUntilDeadline));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.DraftDeadlineApproaching;

        fired.ShouldBe(shouldFire);
    }

    // ---- ADR 0092 D5: effectiveWaitDays (a follow-up resets the wait) ----

    [Fact]
    public void Evaluate_NoResponseWouldFireButRecentFollowUpResetsWait_ReturnsNone()
    {
        // A Submitted app 30 days past its last status change (signal 4 would fire)
        // AND 30 days past the apply date (signal 3 would fire), but a follow-up 2
        // days ago pulls BOTH effective waits below their thresholds → None. This is
        // the whole point of ADR 0092 D5: logging a follow-up lifts the card out of
        // the "kräver åtgärd" queue.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-30), appliedAt: Now.AddDays(-30),
            ghostedThresholdDays: 21, lastFollowUpAt: Now.AddDays(-2));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Theory]
    [InlineData(20, false)] // follow-up 20 days ago → effective wait 20 < 21 → signal 4 quiet
    [InlineData(21, true)]  // 21 days ago → ≥ threshold → fires
    [InlineData(22, true)]  // above
    public void Evaluate_NoResponseBoundaryDrivenByFollowUp_FiresAtOrAboveThreshold(
        int daysSinceFollowUp, bool shouldFire)
    {
        // The status change is old (60 days — would fire on its own), but a more
        // recent follow-up is the effective anchor: signal 4 keys on now − follow-up.
        // Below the threshold the nudge (signal 3) takes over, so we assert on signal
        // 4 specifically (mirrors the anchor-driven boundary tests above).
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-60), appliedAt: Now.AddDays(-60),
            ghostedThresholdDays: 21, lastFollowUpAt: Now.AddDays(-daysSinceFollowUp));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.NoResponseLong;

        fired.ShouldBe(shouldFire);
    }

    [Theory]
    [InlineData(6, false)] // follow-up 6 days ago → effective wait 6 < 7 → nudge quiet
    [InlineData(7, true)]  // 7 days ago → ≥ threshold → fires
    [InlineData(8, true)]  // above
    public void Evaluate_ProactiveNudgeBoundaryDrivenByFollowUp_FiresAtOrAboveThreshold(
        int daysSinceFollowUp, bool shouldFire)
    {
        // Applied long ago (60 days — would nudge on its own) but the status change is
        // recent, so signal 4 stays quiet and signal 3 is in play. The follow-up is
        // the effective anchor for the nudge: it keys on now − follow-up.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now, appliedAt: Now.AddDays(-60),
            ghostedThresholdDays: 21, lastFollowUpAt: Now.AddDays(-daysSinceFollowUp));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.ProactiveFollowUpNudge;

        fired.ShouldBe(shouldFire);
    }

    [Fact]
    public void Evaluate_FollowUpOlderThanStatusChange_DoesNotResetNoResponse_StillFires()
    {
        // A follow-up that PREDATES the status change (older than the anchor): max()
        // picks the anchor, so the effective wait is unchanged and NoResponseLong
        // still fires. Guards against a min/max inversion in EffectiveWait.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-21), appliedAt: Now,
            ghostedThresholdDays: 21, lastFollowUpAt: Now.AddDays(-40));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseLong);
    }

    [Fact]
    public void Evaluate_FollowUpOlderThanAppliedAt_DoesNotResetNudge_StillFires()
    {
        // Nudge parity for the "older follow-up" case: a follow-up predating the apply
        // date leaves the nudge anchor (AppliedAt) as the max → the nudge still fires.
        // Status change recent so signal 4 stays quiet and the nudge is isolated.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now, appliedAt: Now.AddDays(-7),
            ghostedThresholdDays: 21, lastFollowUpAt: Now.AddDays(-30));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.ProactiveFollowUpNudge);
    }

    [Fact]
    public void Evaluate_NullLastFollowUpAt_BehavesAsBeforeD5_NoResponseStillFires()
    {
        // Explicit regression pin for the null (no-follow-up) path: with LastFollowUpAt
        // null the effective wait is exactly now − anchor, so a Submitted app past the
        // ghosted threshold fires NoResponseLong just as it did pre-ADR-0092-D5.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-21), appliedAt: Now,
            ghostedThresholdDays: 21, lastFollowUpAt: null);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseLong);
    }

    // ---- Guards ----

    [Fact]
    public void Evaluate_NullApplication_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            ApplicationAttentionEvaluator.Evaluate(null!, Options, Now));

    [Fact]
    public void Evaluate_NullOptions_Throws()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now, appliedAt: Now);
        Should.Throw<ArgumentNullException>(() =>
            ApplicationAttentionEvaluator.Evaluate(dto, null!, Now));
    }

    // ---- Builder ----

    private static ApplicationDto Dto(
        ApplicationStatus status,
        DateTimeOffset lastStatusChangeAt,
        DateTimeOffset? appliedAt = null,
        bool hasOverdueFollowUp = false,
        int ghostedThresholdDays = 21,
        DateTimeOffset? expiresAt = null,
        // ADR 0092 D5: the denormalised last-follow-up scalar (null = no follow-up
        // yet). Drives effectiveWaitDays = now − max(anchor, lastFollowUpAt) in
        // signals 4 (no-response) and 3 (nudge). Defaults to null so every existing
        // signal test exercises the null (no-follow-up) path unchanged (regression).
        DateTimeOffset? lastFollowUpAt = null)
    {
        JobAdSummaryDto? jobAd = expiresAt is null
            ? null
            : new JobAdSummaryDto(
                Guid.NewGuid(), "Utvecklare", "Acme", null, "Platsbanken", null, expiresAt);

        return new ApplicationDto(
            Id: Guid.NewGuid(),
            JobSeekerId: Guid.NewGuid(),
            JobAdId: jobAd?.JobAdId,
            Status: status.Name,
            CreatedAt: lastStatusChangeAt,
            UpdatedAt: lastStatusChangeAt,
            JobAd: jobAd,
            AppliedAt: appliedAt,
            LastStatusChangeAt: lastStatusChangeAt,
            HasOverdueFollowUp: hasOverdueFollowUp,
            GhostedThresholdDays: ghostedThresholdDays,
            LastFollowUpAt: lastFollowUpAt);
    }
}
