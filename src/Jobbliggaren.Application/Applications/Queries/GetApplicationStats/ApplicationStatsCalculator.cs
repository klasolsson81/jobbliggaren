using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Queries.GetApplicationStats;

/// <summary>
/// Pure, deterministic calculator for the application-statistics read model
/// (issue #313). A function of a materialised row set plus a reference instant —
/// NO database, NO AI, NO clock (the instant is passed in). The SSOT for every
/// metric definition, kept separate from the I/O handler (mirrors
/// <see cref="Attention.ApplicationAttentionEvaluator"/>) so the civic-honesty-
/// critical math is unit-testable without EF (senior-cto-advisor bind
/// 2026-06-29, Approach B + §2.4).
///
/// Funnel honesty (Q2 implementation note): "reached stage X" is SET MEMBERSHIP
/// on the success spine, NOT an ordinal <c>&gt;=</c> test. Rejected(8)/
/// Withdrawn(9)/Ghosted(10) carry higher ordinals than Accepted(7) but are NOT
/// further along the success spine, so an off-spine terminal is credited only at
/// the "Sent" stage (the aggregate persists no stage history). That v1 under-count
/// is surfaced via <see cref="ApplicationStatsDto.OffFunnelExitCount"/>, never
/// silently mis-reported (§5).
/// </summary>
public static class ApplicationStatsCalculator
{
    // Funnel stage keys — stable contract strings the FE maps to Swedish labels.
    public const string StageSent = "Sent";
    public const string StageResponded = "Responded";
    public const string StageInterview = "Interview";
    public const string StageOffer = "Offer";
    public const string StageAccepted = "Accepted";

    private const int MonthsInSeries = 12;

    // Success-spine membership sets, built from the SmartEnum (no magic status
    // strings — §5). "Responded" = the employer engaged at all (Acknowledged or
    // beyond); each subsequent stage is the cumulative reach. Draft and the
    // off-spine terminals (Rejected/Withdrawn/Ghosted) are deliberately absent —
    // they contribute only to "Sent" (if ever submitted).
    private static readonly HashSet<string> RespondedOrBeyond =
    [
        ApplicationStatus.Acknowledged.Name,
        ApplicationStatus.InterviewScheduled.Name,
        ApplicationStatus.Interviewing.Name,
        ApplicationStatus.OfferReceived.Name,
        ApplicationStatus.Accepted.Name,
    ];

    private static readonly HashSet<string> InterviewOrBeyond =
    [
        ApplicationStatus.InterviewScheduled.Name,
        ApplicationStatus.Interviewing.Name,
        ApplicationStatus.OfferReceived.Name,
        ApplicationStatus.Accepted.Name,
    ];

    private static readonly HashSet<string> OfferOrBeyond =
    [
        ApplicationStatus.OfferReceived.Name,
        ApplicationStatus.Accepted.Name,
    ];

    private static readonly HashSet<string> OffFunnelTerminals =
    [
        ApplicationStatus.Rejected.Name,
        ApplicationStatus.Withdrawn.Name,
        ApplicationStatus.Ghosted.Name,
    ];

    public static ApplicationStatsDto Calculate(
        IReadOnlyList<ApplicationStatRow> rows, DateTimeOffset now)
    {
        var totalApplications = rows.Count;

        // TotalSent is the canonical rate/funnel denominator: a never-sent draft
        // cannot have been responded to or rejected (Q4). AppliedAt is the
        // idempotent first-submit stamp.
        var sentRows = rows.Where(r => r.AppliedAt is not null).ToList();
        var totalSent = sentRows.Count;

        var statusCounts = BuildStatusCounts(rows);

        // Numerators over SENT rows only.
        var respondedCount = sentRows.Count(r => RespondedOrBeyond.Contains(r.Status));
        var interviewCount = sentRows.Count(r => InterviewOrBeyond.Contains(r.Status));
        var offerCount = sentRows.Count(r => OfferOrBeyond.Contains(r.Status));
        var acceptedCount = sentRows.Count(r => r.Status == ApplicationStatus.Accepted.Name);
        var rejectedCount = sentRows.Count(r => r.Status == ApplicationStatus.Rejected.Name);
        var offFunnelExitCount = sentRows.Count(r => OffFunnelTerminals.Contains(r.Status));

        var funnel = new List<FunnelStageDto>
        {
            new(StageSent, totalSent, Percent(totalSent, totalSent)),
            new(StageResponded, respondedCount, Percent(respondedCount, totalSent)),
            new(StageInterview, interviewCount, Percent(interviewCount, totalSent)),
            new(StageOffer, offerCount, Percent(offerCount, totalSent)),
            new(StageAccepted, acceptedCount, Percent(acceptedCount, totalSent)),
        };

        return new ApplicationStatsDto(
            totalApplications,
            totalSent,
            statusCounts,
            // Rejection rate counts ONLY current-status Rejected — Withdrawn (user
            // exit) and Ghosted (no response) are NOT folded in (Q4; §5 honesty).
            ResponseRate: new ApplicationRateDto(respondedCount, totalSent, Percent(respondedCount, totalSent)),
            InterviewRate: new ApplicationRateDto(interviewCount, totalSent, Percent(interviewCount, totalSent)),
            RejectionRate: new ApplicationRateDto(rejectedCount, totalSent, Percent(rejectedCount, totalSent)),
            funnel,
            offFunnelExitCount,
            BuildMonthlySeries(sentRows, now));
    }

    // All ten statuses, ordinal order, zero-filled — a stable, complete table
    // (drafts included for raw transparency).
    private static List<StatusCountDto> BuildStatusCounts(IReadOnlyList<ApplicationStatRow> rows)
    {
        var countByName = rows
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        return ApplicationStatus.List
            .OrderBy(s => s.Value)
            .Select(s => new StatusCountDto(
                s.Name, countByName.GetValueOrDefault(s.Name, 0)))
            .ToList();
    }

    // Rolling last 12 months, [monthStart, nextMonthStart) half-open, UTC-derived
    // — consistent with GetActivityReportQueryHandler's AppliedAt windowing.
    // Zero-filled and ordered oldest → newest. Drafts (null AppliedAt) are absent
    // from sentRows, so they never appear in the series (a draft was sent in no
    // month). Applications applied before the window fall in no bucket.
    private static List<MonthlyApplicationCountDto> BuildMonthlySeries(
        IReadOnlyList<ApplicationStatRow> sentRows, DateTimeOffset now)
    {
        var currentMonthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var windowStart = currentMonthStart.AddMonths(-(MonthsInSeries - 1));

        var buckets = new List<MonthlyApplicationCountDto>(MonthsInSeries);
        for (var i = 0; i < MonthsInSeries; i++)
        {
            var monthStart = windowStart.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1);
            var count = sentRows.Count(r =>
                r.AppliedAt!.Value >= monthStart && r.AppliedAt.Value < monthEnd);
            buckets.Add(new MonthlyApplicationCountDto(monthStart.Year, monthStart.Month, count));
        }
        return buckets;
    }

    // Rounded integer percent; 0 when the denominator is 0 (FE renders that as a
    // neutral "inga skickade ännu", never "0 %").
    private static int Percent(int numerator, int denominator) =>
        denominator == 0
            ? 0
            : (int)Math.Round(100.0 * numerator / denominator, MidpointRounding.AwayFromZero);
}
