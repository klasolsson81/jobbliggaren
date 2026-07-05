using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Attention;

/// <summary>
/// Pure unit tests for the /ansokningar attention-prioritisation rule (design §11
/// "Urgensregler", superseding ADR 0085 §3; #630 PR 4). No DbContext, no clock —
/// <c>now</c> is injected, so the whole policy is exercised in-memory (CLAUDE.md §2.4).
/// Priority order: offer → overdue follow-up → draft deadline → ghost-suggest →
/// no-response nudge → silent-after-interview.
/// </summary>
public class ApplicationAttentionEvaluatorTests
{
    // Fixed anchor — all relative ages are computed from this instant.
    private static readonly DateTimeOffset Now =
        new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    // Defaults: NoResponseNudgeDays 14, GhostSuggestDays 30, SilentAfterInterviewDays 7, DraftDeadlineDays 7.
    private static readonly ApplicationAttentionOptions Options = new();

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
        // Priority order: offer (1) before overdue follow-up (2).
        var dto = Dto(ApplicationStatus.OfferReceived, lastStatusChangeAt: Now, hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OfferAwaitingReply);
    }

    // ---- Signal 2: Overdue scheduled follow-up ----

    [Fact]
    public void Evaluate_SubmittedWithOverdueFollowUp_ReturnsOverdueFollowUp()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now, hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    [Fact]
    public void Evaluate_OverdueFollowUpBeatsGhostSuggest_OnAnAgedApplication()
    {
        // An explicit scheduled follow-up (signal 2) outranks the derived
        // ghost-suggest silence heuristic (signal 4), even 40 days silent.
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now.AddDays(-40),
            hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    [Fact]
    public void Evaluate_InterviewingWithOverdueFollowUp_OverdueWinsOverSilentAfterInterview()
    {
        // The one genuine cross-signal overlap for silent-after-interview (CTO Q2):
        // an Interviewing app long silent (signal 6 would fire) but with an overdue
        // scheduled follow-up → OverdueFollowUp (2) outranks SilentAfterInterview (6).
        var dto = Dto(ApplicationStatus.Interviewing, lastStatusChangeAt: Now.AddDays(-30),
            hasOverdueFollowUp: true);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    [Fact]
    public void Evaluate_DraftWithOverdueFollowUpAndApproachingDeadline_OverdueWinsByPriority()
    {
        // A draft can hold a follow-up (AddFollowUp is blocked only for closed
        // applications). Priority order: overdue follow-up (2) before draft deadline (3).
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now,
            hasOverdueFollowUp: true, expiresAt: Now.AddDays(2));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.OverdueFollowUp);
    }

    // ---- Signal 3: Draft with approaching deadline (+ the draft-only invariant) ----

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
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: Now.AddDays(-1));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Fact]
    public void Evaluate_DraftWithNoJobAd_ReturnsNone()
    {
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: null);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    /// <summary>
    /// THE BINDING INVARIANT (design §11, Klas 2026-06-28): the application deadline
    /// is attention-relevant ONLY for drafts. A submitted application whose deadline
    /// is approaching must NEVER surface a draft-deadline signal — once submitted the
    /// deadline is moot. This test pins it against regression.
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
        // Keep the other signals quiet: recent status change, no overdue follow-up.
        var dto = Dto(status, lastStatusChangeAt: Now, appliedAt: Now, expiresAt: Now.AddDays(1));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldNotBe(ApplicationAttentionSignal.DraftDeadlineApproaching);
    }

    [Theory]
    [InlineData(6, true)]   // within: 6 days ≤ 7-day window
    [InlineData(7, true)]   // at: ≤ fires
    [InlineData(8, false)]  // beyond: 8 days > 7-day window
    public void Evaluate_DraftDeadlineBoundary_FiresAtOrWithinThreshold(int daysUntilDeadline, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Draft, lastStatusChangeAt: Now, expiresAt: Now.AddDays(daysUntilDeadline));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.DraftDeadlineApproaching;

        fired.ShouldBe(shouldFire);
    }

    // ---- Signal 4 (ghost-suggest, 30) + Signal 5 (no-response nudge, 14) ----

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    public void Evaluate_AwaitingEmployerBeyondGhostSuggest_ReturnsGhostSuggested(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-30));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.GhostSuggested);
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    public void Evaluate_AwaitingEmployerBetweenNudgeAndGhost_ReturnsNoResponseNudge(string statusName)
    {
        // 20 days silent: past the 14-day nudge, below the 30-day ghost-suggest.
        var status = ApplicationStatus.FromName(statusName);
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-20));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseNudge);
    }

    [Fact]
    public void Evaluate_GhostSuggestSubsumesNudge_BothWouldFire_GhostWinsByPriority()
    {
        // THE subsumption constraint (CTO Q2, correctness not preference): 35 days
        // silent clears BOTH the 14-day nudge and the 30-day ghost-suggest window.
        // Ghost-suggest is checked first, so it wins — otherwise the shorter nudge
        // threshold would make ghost-suggest unreachable on every aged application.
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now.AddDays(-35));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.GhostSuggested);
    }

    [Theory]
    [InlineData(29, false)] // below: 29 days < 30-day ghost-suggest → nudge, not ghost
    [InlineData(30, true)]  // at: ≥ fires
    [InlineData(31, true)]  // above
    public void Evaluate_GhostSuggestBoundary_FiresAtOrAboveThreshold(int daysSilent, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now.AddDays(-daysSilent));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.GhostSuggested;

        fired.ShouldBe(shouldFire);
    }

    [Theory]
    [InlineData(13, false)] // below: 13 days < 14-day nudge → None
    [InlineData(14, true)]  // at: ≥ fires
    [InlineData(29, true)]  // above but below ghost-suggest → still the nudge
    public void Evaluate_NoResponseNudgeBoundary_FiresBetweenNudgeAndGhost(int daysSilent, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now.AddDays(-daysSilent));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.NoResponseNudge;

        fired.ShouldBe(shouldFire);
    }

    [Fact]
    public void Evaluate_RecentSubmittedBelowNudge_ReturnsNone()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now.AddDays(-10));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    // ---- Signal 6: Silent after interview (Interviewing only) ----

    [Fact]
    public void Evaluate_InterviewingBeyondSilentWindow_ReturnsSilentAfterInterview()
    {
        var dto = Dto(ApplicationStatus.Interviewing, lastStatusChangeAt: Now.AddDays(-7));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.SilentAfterInterview);
    }

    [Fact]
    public void Evaluate_InterviewingWithinSilentWindow_ReturnsNone()
    {
        var dto = Dto(ApplicationStatus.Interviewing, lastStatusChangeAt: Now.AddDays(-6));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Fact]
    public void Evaluate_InterviewScheduledSilentLong_ReturnsNone()
    {
        // Silent-after-interview is Interviewing-ONLY (design §11). InterviewScheduled
        // is active but not "silent after interview"; it must stay None even 60 days on.
        // Also pins that the no-response signals never fire outside Submitted/Acknowledged.
        var dto = Dto(ApplicationStatus.InterviewScheduled, lastStatusChangeAt: Now.AddDays(-60));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Theory]
    [InlineData(6, false)] // below: 6 days < 7-day silent window
    [InlineData(7, true)]  // at: ≥ fires
    [InlineData(8, true)]  // above
    public void Evaluate_SilentAfterInterviewBoundary_FiresAtOrAboveThreshold(int daysSilent, bool shouldFire)
    {
        var dto = Dto(ApplicationStatus.Interviewing, lastStatusChangeAt: Now.AddDays(-daysSilent));

        var fired = ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            == ApplicationAttentionSignal.SilentAfterInterview;

        fired.ShouldBe(shouldFire);
    }

    // ---- None: terminal / non-actionable ----

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Accepted")]
    [InlineData("Ghosted")]
    public void Evaluate_TerminalOrGhostedStatus_ReturnsNone(string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        var dto = Dto(status, lastStatusChangeAt: Now.AddDays(-60), appliedAt: Now.AddDays(-60));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    // ---- ADR 0092 D5: effectiveWaitDays (a follow-up resets the wait) ----

    [Fact]
    public void Evaluate_GhostSuggestWouldFireButRecentFollowUpResetsWait_ReturnsNone()
    {
        // A Submitted app 35 days past its last status change (ghost-suggest would
        // fire) but a follow-up 2 days ago pulls the effective wait below every
        // threshold → None. This is the point of ADR 0092 D5: logging a follow-up
        // lifts the card out of the "kräver åtgärd" queue.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-35), lastFollowUpAt: Now.AddDays(-2));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Fact]
    public void Evaluate_FollowUpPullsGhostSuggestDownIntoNudgeRange_ReturnsNoResponseNudge()
    {
        // 40 days silent (ghost-suggest on its own) but a follow-up 20 days ago is the
        // effective anchor: effective wait 20 → past nudge (14), below ghost (30) → nudge.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-40), lastFollowUpAt: Now.AddDays(-20));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.NoResponseNudge);
    }

    [Fact]
    public void Evaluate_FollowUpResetsSilentAfterInterview_ReturnsNone()
    {
        // Silent-after-interview keys on the same effective-wait clock: an Interviewing
        // app 30 days silent but with a follow-up 2 days ago drops below the window.
        var dto = Dto(ApplicationStatus.Interviewing,
            lastStatusChangeAt: Now.AddDays(-30), lastFollowUpAt: Now.AddDays(-2));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.None);
    }

    [Fact]
    public void Evaluate_FollowUpOlderThanStatusChange_DoesNotResetWait_GhostSuggestStillFires()
    {
        // A follow-up that PREDATES the status change (older than the anchor): max()
        // picks the anchor, so the effective wait is unchanged and ghost-suggest still
        // fires. Guards against a min/max inversion in EffectiveWait.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-30), lastFollowUpAt: Now.AddDays(-45));

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.GhostSuggested);
    }

    [Fact]
    public void Evaluate_NullLastFollowUpAt_UsesAnchorWait_GhostSuggestFires()
    {
        // Explicit regression pin for the null (no-follow-up) path: with LastFollowUpAt
        // null the effective wait is exactly now − anchor.
        var dto = Dto(ApplicationStatus.Submitted,
            lastStatusChangeAt: Now.AddDays(-30), lastFollowUpAt: null);

        ApplicationAttentionEvaluator.Evaluate(dto, Options, Now)
            .ShouldBe(ApplicationAttentionSignal.GhostSuggested);
    }

    // ---- Guards ----

    [Fact]
    public void Evaluate_NullApplication_Throws() =>
        Should.Throw<ArgumentNullException>(() =>
            ApplicationAttentionEvaluator.Evaluate(null!, Options, Now));

    [Fact]
    public void Evaluate_NullOptions_Throws()
    {
        var dto = Dto(ApplicationStatus.Submitted, lastStatusChangeAt: Now);
        Should.Throw<ArgumentNullException>(() =>
            ApplicationAttentionEvaluator.Evaluate(dto, null!, Now));
    }

    // ---- Builder ----

    private static ApplicationDto Dto(
        ApplicationStatus status,
        DateTimeOffset lastStatusChangeAt,
        DateTimeOffset? appliedAt = null,
        bool hasOverdueFollowUp = false,
        DateTimeOffset? expiresAt = null,
        // ADR 0092 D5: the denormalised last-follow-up scalar (null = no follow-up
        // yet). Drives effectiveWaitDays = now − max(LastStatusChangeAt, lastFollowUpAt)
        // in the no-response and silent-after-interview signals. Defaults to null so
        // every anchor-driven test exercises the null (no-follow-up) path unchanged.
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
            LastFollowUpAt: lastFollowUpAt);
    }
}
