using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — turns our own counts into what the per-kind region may show. Pure and static.
/// <para>
/// <b>The gate against a silent zero.</b> A count that fails to write is contained and logged, so a zero cannot by
/// itself be told from a period without mail. When our total for the period is zero and the provider's own count for the
/// same period shows at least one sent mail, nothing is shown. The provider's figure is only a witness here: it is never
/// shown, subtracted or summed (ADR 0178 D1). The gate does nothing unless the provider's read is observed; it catches a
/// total loss, not a partial one.
/// </para>
/// </summary>
public static class EmailSendCountsEvaluator
{
    /// <summary>The order the kinds are listed in: the account mails first, then notifications, then feedback.</summary>
    public static IReadOnlyList<EmailKind> KindOrder { get; } =
    [
        EmailKind.LoginChallenge,
        EmailKind.AccountEmailChangeRequestedNotification,
        EmailKind.EmailChangedNotification,
        EmailKind.MatchNotification,
        EmailKind.FollowedCompanyNotification,
        EmailKind.FeedbackReceivedNotification,
        EmailKind.FeedbackReply,
    ];

    /// <summary>Whether the provider must be asked before the counts may be shown: only when they total zero.</summary>
    public static bool NeedsWitness(EmailSendCountsRead own) =>
        own is EmailSendCountsRead.Observed observed && observed.Rows.Sum(row => (long)row.Attempts) == 0;

    /// <summary>
    /// The region for one period. <paramref name="fromStart"/> is the instant the first date begins in Stockholm;
    /// <paramref name="witness"/> is the provider's read of the same period, required exactly when
    /// <see cref="NeedsWitness"/> says so.
    /// </summary>
    public static EmailSendCountsDto Evaluate(
        EmailDeliveryPeriod period,
        DateOnly from,
        DateTimeOffset fromStart,
        EmailSendCountsRead own,
        EmailDeliveryStatisticsRead? witness)
    {
        if (own is not EmailSendCountsRead.Observed observed)
        {
            return EmailSendCountsDto.NotObserved(period);
        }

        if (NeedsWitness(own))
        {
            if (witness is null)
            {
                throw new ArgumentException("A zero total is shown only after the provider's read.", nameof(witness));
            }

            if (witness is EmailDeliveryStatisticsRead.Observed { SentCount: >= 1 })
            {
                return EmailSendCountsDto.NotObservable(period, from);
            }
        }

        var coveredFrom = observed.RecordingStartedAt <= fromStart ? (DateTimeOffset?)null : observed.RecordingStartedAt;
        return EmailSendCountsDto.Observed(period, from, coveredFrom, [.. KindOrder.Select(kind => CountOf(kind, observed.Rows))]);
    }

    private static EmailKindCountDto CountOf(EmailKind kind, IReadOnlyList<EmailSendCountRow> rows)
    {
        var ofKind = rows.Where(row => row.Kind == kind).ToList();
        var failures = ofKind.Where(row => row.Outcome != EmailSendOutcome.Accepted).ToList();
        var accepted = ofKind.Where(row => row.Outcome == EmailSendOutcome.Accepted).Sum(row => (long)row.Attempts);
        var failed = failures.Sum(row => (long)row.Attempts);

        EmailLatestFailureDto? latest = null;
        if (failures.Count > 0)
        {
            var day = failures.Max(row => row.Day);
            var seen = failures.Where(row => row.Day == day).Select(row => row.Outcome).ToHashSet();
            latest = new EmailLatestFailureDto(day, [.. EmailSendOutcomes.FailureClasses.Where(seen.Contains)]);
        }

        return new EmailKindCountDto(kind, accepted, failed, latest);
    }
}
