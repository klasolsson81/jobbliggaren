using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// Pure attention-prioritisation policy for /ansokningar (ADR 0085 §3). Given a
/// read-side <see cref="ApplicationDto"/>, the configured thresholds and the
/// current instant, it returns the single highest-priority reason the
/// application needs action now (or <see cref="ApplicationAttentionSignal.None"/>).
///
/// <para>
/// This is view-derivation policy, not an aggregate invariant: thresholds are
/// operator-tunable config and "attention" is a presentation concern, so it
/// lives in the Application layer next to the read DTO, not in the Domain. It is
/// deliberately pure (no DbContext, no clock, no DOM) — the caller injects
/// <c>now</c> via <see cref="IDateTimeProvider"/> — so the whole rule is unit
/// testable without a database (CLAUDE.md §2.4).
/// </para>
/// </summary>
public static class ApplicationAttentionEvaluator
{
    /// <summary>
    /// Returns the highest-priority attention signal for <paramref name="application"/>,
    /// evaluated in the locked ADR 0085 §3 priority order
    /// (offer → overdue follow-up → draft deadline → no response → proactive nudge),
    /// or <see cref="ApplicationAttentionSignal.None"/> when no signal fires.
    /// </summary>
    public static ApplicationAttentionSignal Evaluate(
        ApplicationDto application,
        ApplicationAttentionOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var status = ApplicationStatus.FromName(application.Status);

        // (1) Offer awaiting reply.
        if (status == ApplicationStatus.OfferReceived)
            return ApplicationAttentionSignal.OfferAwaitingReply;

        // (2) Overdue scheduled follow-up — projected once at the read boundary
        // as a correlated EXISTS (a Pending follow-up whose ScheduledAt has passed).
        if (application.HasOverdueFollowUp)
            return ApplicationAttentionSignal.OverdueFollowUp;

        // (5) Draft with approaching deadline — DRAFT-ONLY by design (ADR 0085 §3,
        // Klas binding invariant): the application deadline ("sök senast") is
        // attention-relevant only while the application is still a draft. Once
        // submitted it is moot and must never surface here. Pinned by a unit test.
        if (status == ApplicationStatus.Draft
            && application.JobAd?.ExpiresAt is { } expiresAt
            && expiresAt >= now
            && expiresAt - now <= TimeSpan.FromDays(options.DraftDeadlineDays))
        {
            return ApplicationAttentionSignal.DraftDeadlineApproaching;
        }

        // (4) No response for long — submitted/acknowledged and the EFFECTIVE wait
        // (ADR 0092 D5) since the last status change OR the last follow-up, whichever
        // is more recent, is at least the per-aggregate ghosted threshold (reused,
        // not a new config value — ADR 0085 §3). Logging a follow-up resets it.
        if (IsAwaitingEmployer(status)
            && EffectiveWait(application.LastStatusChangeAt, application.LastFollowUpAt, now)
               >= TimeSpan.FromDays(application.GhostedThresholdDays))
        {
            return ApplicationAttentionSignal.NoResponseLong;
        }

        // (3) Proactive follow-up nudge — submitted/acknowledged long enough since
        // the apply date (or the last follow-up, whichever is more recent — ADR 0092
        // D5) that a follow-up is due. Proactive by design: it fires even when the
        // user scheduled no follow-up (Klas: "hamnar bara i en hög"). No AppliedAt
        // (defensive — a submitted application always has one) → no anchor → no nudge.
        if (IsAwaitingEmployer(status)
            && application.AppliedAt is { } appliedAt
            && EffectiveWait(appliedAt, application.LastFollowUpAt, now)
               >= TimeSpan.FromDays(options.FollowUpNudgeDays))
        {
            return ApplicationAttentionSignal.ProactiveFollowUpNudge;
        }

        return ApplicationAttentionSignal.None;
    }

    private static bool IsAwaitingEmployer(ApplicationStatus status) =>
        status == ApplicationStatus.Submitted || status == ApplicationStatus.Acknowledged;

    /// <summary>
    /// ADR 0092 D5: the effective wait = min(time since the anchor event, time since
    /// the last follow-up) = <c>now − max(anchor, lastFollowUpAt)</c>. A follow-up
    /// more recent than the anchor shrinks the wait, so logging one lifts the card
    /// out of the queue until the threshold is crossed again. With no follow-up it is
    /// simply <c>now − anchor</c>.
    /// </summary>
    private static TimeSpan EffectiveWait(
        DateTimeOffset anchor, DateTimeOffset? lastFollowUpAt, DateTimeOffset now)
    {
        var reference = lastFollowUpAt is { } f && f > anchor ? f : anchor;
        return now - reference;
    }
}
