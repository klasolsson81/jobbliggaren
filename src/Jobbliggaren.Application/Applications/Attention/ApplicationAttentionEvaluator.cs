using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// Pure attention-prioritisation policy for /ansokningar (design §11 "Urgensregler",
/// superseding ADR 0085 §3; CTO-bound 2026-07-05). Given a read-side
/// <see cref="ApplicationDto"/>, the configured thresholds and the current instant,
/// it returns the single highest-priority reason the application needs action now
/// (or <see cref="ApplicationAttentionSignal.None"/>).
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
    /// evaluated in the locked design §11 priority order (offer → overdue follow-up →
    /// draft deadline → ghost-suggest → no-response nudge → silent-after-interview),
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

        // (1) Offer awaiting reply — fires unconditionally on OfferReceived.
        if (status == ApplicationStatus.OfferReceived)
            return ApplicationAttentionSignal.OfferAwaitingReply;

        // (2) Overdue scheduled follow-up — projected once at the read boundary
        // as a correlated EXISTS (a Pending follow-up whose ScheduledAt has passed).
        // An explicit scheduled follow-up outranks the derived silence heuristics.
        if (application.HasOverdueFollowUp)
            return ApplicationAttentionSignal.OverdueFollowUp;

        // (3) Draft with approaching deadline — DRAFT-ONLY by design (design §11,
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

        // (4)+(5) No-response signals for submitted/acknowledged applications. Both
        // share one EFFECTIVE wait (ADR 0092 D5): now − max(last status change, last
        // follow-up), so a logged follow-up resets the clock. Ghost-suggest
        // (≥ GhostSuggestDays) is checked BEFORE the nudge (≥ NoResponseNudgeDays)
        // because the larger window subsumes the smaller — checking the nudge first
        // would make ghost-suggest unreachable (correctness, not preference).
        if (IsAwaitingEmployer(status))
        {
            var wait = EffectiveWait(application.LastStatusChangeAt, application.LastFollowUpAt, now);

            // (4) Ghost-suggest — no response for at least the ghost-suggest window.
            if (wait >= TimeSpan.FromDays(options.GhostSuggestDays))
                return ApplicationAttentionSignal.GhostSuggested;

            // (5) No-response nudge — no response long enough that a follow-up is due.
            if (wait >= TimeSpan.FromDays(options.NoResponseNudgeDays))
                return ApplicationAttentionSignal.NoResponseNudge;
        }

        // (6) Silent after interview — an Interviewing application with no status
        // change (reset by a logged follow-up — same effective-wait clock) for at
        // least the silent window. Status-disjoint from the no-response signals
        // above; ranked last per the design §11 presentation order.
        if (status == ApplicationStatus.Interviewing
            && EffectiveWait(application.LastStatusChangeAt, application.LastFollowUpAt, now)
               >= TimeSpan.FromDays(options.SilentAfterInterviewDays))
        {
            return ApplicationAttentionSignal.SilentAfterInterview;
        }

        return ApplicationAttentionSignal.None;
    }

    private static bool IsAwaitingEmployer(ApplicationStatus status) =>
        status == ApplicationStatus.Submitted || status == ApplicationStatus.Acknowledged;

    /// <summary>
    /// ADR 0092 D5: the effective wait = <c>now − max(anchor, lastFollowUpAt)</c>.
    /// A follow-up more recent than the anchor shrinks the wait, so logging one lifts
    /// the card out of the queue until the threshold is crossed again. With no
    /// follow-up it is simply <c>now − anchor</c>. The anchor is the last status
    /// change (design §11 "senaste händelse") for every derived-silence signal.
    /// </summary>
    private static TimeSpan EffectiveWait(
        DateTimeOffset anchor, DateTimeOffset? lastFollowUpAt, DateTimeOffset now)
    {
        var reference = lastFollowUpAt is { } f && f > anchor ? f : anchor;
        return now - reference;
    }
}
