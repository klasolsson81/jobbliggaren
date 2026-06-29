using Jobbliggaren.Application.Applications.Queries.GetApplicationStats;
using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Queries.GetApplicationStats;

// #313 — pure metric calculator for the application-statistics read model.
// Deterministic projection (NO AI, NO DB, NO clock — the instant is passed in).
// This is the civic-honesty-critical math (rate denominators, the off-spine
// funnel approximation, the 12-month window) and is unit-tested here without EF
// per the senior-cto-advisor bind 2026-06-29 (Approach B + §2.4). The handler's
// wiring (scoping, soft-delete, anonymous→empty) is covered separately on the EF
// InMemory provider.
public class ApplicationStatsCalculatorTests
{
    // Fixed reference instant for the monthly-series tests: 2026-06-15 ⇒ current
    // month = June 2026, window = July 2025 … June 2026 (12 months).
    private static readonly DateTimeOffset Now =
        new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    // A "sent" application has a non-null AppliedAt; a Draft has null. In the real
    // lifecycle every non-Draft status implies AppliedAt is stamped, so the test
    // data mirrors that.
    private static ApplicationStatRow Row(ApplicationStatus status, DateTimeOffset? appliedAt) =>
        new(status.Name, appliedAt);

    private static ApplicationStatRow Draft() =>
        new(ApplicationStatus.Draft.Name, null);

    private static ApplicationStatRow Sent(ApplicationStatus status) =>
        new(status.Name, new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero));

    private static int CountOf(ApplicationStatsDto dto, ApplicationStatus status) =>
        dto.StatusCounts.Single(s => s.Status == status.Name).Count;

    private static FunnelStageDto Stage(ApplicationStatsDto dto, string stage) =>
        dto.Funnel.Single(f => f.Stage == stage);

    // ---------------------------------------------------------------
    // Empty set — all zero, complete shape
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_EmptySet_ReturnsAllZeroWithCompleteShape()
    {
        var dto = ApplicationStatsCalculator.Calculate([], Now);

        dto.TotalApplications.ShouldBe(0);
        dto.TotalSent.ShouldBe(0);
        // All ten statuses present, every count zero.
        dto.StatusCounts.Count.ShouldBe(10);
        dto.StatusCounts.ShouldAllBe(s => s.Count == 0);
        // Rates: denominator 0 → percent 0 (FE renders "inga skickade ännu").
        dto.ResponseRate.ShouldBe(new ApplicationRateDto(0, 0, 0));
        dto.InterviewRate.ShouldBe(new ApplicationRateDto(0, 0, 0));
        dto.RejectionRate.ShouldBe(new ApplicationRateDto(0, 0, 0));
        // Funnel: five stages, all zero.
        dto.Funnel.Count.ShouldBe(5);
        dto.Funnel.ShouldAllBe(f => f.Count == 0 && f.PercentOfSent == 0);
        dto.OffFunnelExitCount.ShouldBe(0);
        // 12 zero-filled month buckets.
        dto.MonthlyApplications.Count.ShouldBe(12);
        dto.MonthlyApplications.ShouldAllBe(m => m.Count == 0);
    }

    // ---------------------------------------------------------------
    // TotalApplications counts drafts; TotalSent excludes them
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_TotalApplications_IncludesDrafts_TotalSent_ExcludesThem()
    {
        ApplicationStatRow[] rows =
        [
            Draft(),
            Draft(),
            Sent(ApplicationStatus.Submitted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.TotalApplications.ShouldBe(3); // drafts counted in the raw total
        dto.TotalSent.ShouldBe(1);         // only the submitted one is "sent"
        CountOf(dto, ApplicationStatus.Draft).ShouldBe(2);
        CountOf(dto, ApplicationStatus.Submitted).ShouldBe(1);
    }

    [Fact]
    public void Calculate_DraftOnly_AllRatesHaveZeroDenominator()
    {
        ApplicationStatRow[] rows = [Draft()];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.TotalApplications.ShouldBe(1);
        dto.TotalSent.ShouldBe(0);
        dto.ResponseRate.Denominator.ShouldBe(0);
        dto.RejectionRate.Denominator.ShouldBe(0);
        dto.RejectionRate.Percent.ShouldBe(0);
        // A draft never reaches the funnel — every stage is zero.
        dto.Funnel.ShouldAllBe(f => f.Count == 0);
    }

    // ---------------------------------------------------------------
    // Per-status counts — all ten, ordinal order, zero-filled
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_StatusCounts_EmitsAllTenInOrdinalOrder()
    {
        var dto = ApplicationStatsCalculator.Calculate([], Now);

        var expectedOrder = ApplicationStatus.List
            .OrderBy(s => s.Value)
            .Select(s => s.Name)
            .ToList();

        dto.StatusCounts.Select(s => s.Status).ShouldBe(expectedOrder);
    }

    // ---------------------------------------------------------------
    // Rejection rate — only Rejected; Withdrawn/Ghosted excluded
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_RejectionRate_CountsOnlyRejected_NotWithdrawnOrGhosted()
    {
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Rejected),
            Sent(ApplicationStatus.Withdrawn), // user exit — NOT a rejection
            Sent(ApplicationStatus.Ghosted),   // no response — NOT a rejection
            Sent(ApplicationStatus.Submitted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.TotalSent.ShouldBe(4);
        // Numerator is the single Rejected; Withdrawn and Ghosted are NOT folded in.
        dto.RejectionRate.Numerator.ShouldBe(1);
        dto.RejectionRate.Denominator.ShouldBe(4);
        dto.RejectionRate.Percent.ShouldBe(25);
        // The two off-funnel exits surface their own honest counts.
        CountOf(dto, ApplicationStatus.Withdrawn).ShouldBe(1);
        CountOf(dto, ApplicationStatus.Ghosted).ShouldBe(1);
    }

    // ---------------------------------------------------------------
    // Response / interview rates — cumulative reach
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_ResponseAndInterviewRates_AreCumulativeReachOverSent()
    {
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Submitted),         // sent, not responded
            Sent(ApplicationStatus.Acknowledged),      // responded, not interview
            Sent(ApplicationStatus.InterviewScheduled),// interview
            Sent(ApplicationStatus.Accepted),          // responded + interview + offer + accepted
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.TotalSent.ShouldBe(4);
        // Responded = Acknowledged or beyond → 3 of 4.
        dto.ResponseRate.Numerator.ShouldBe(3);
        dto.ResponseRate.Denominator.ShouldBe(4);
        dto.ResponseRate.Percent.ShouldBe(75);
        // Interview = InterviewScheduled or beyond → 2 of 4.
        dto.InterviewRate.Numerator.ShouldBe(2);
        dto.InterviewRate.Percent.ShouldBe(50);
    }

    // ---------------------------------------------------------------
    // Funnel — cumulative, monotonic, Accepted reaches every stage
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_Funnel_IsCumulativeAndMonotonic()
    {
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Submitted),
            Sent(ApplicationStatus.Acknowledged),
            Sent(ApplicationStatus.InterviewScheduled),
            Sent(ApplicationStatus.OfferReceived),
            Sent(ApplicationStatus.Accepted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        Stage(dto, ApplicationStatsCalculator.StageSent).Count.ShouldBe(5);      // all sent
        Stage(dto, ApplicationStatsCalculator.StageResponded).Count.ShouldBe(4); // Acknowledged+
        Stage(dto, ApplicationStatsCalculator.StageInterview).Count.ShouldBe(3); // InterviewScheduled+
        Stage(dto, ApplicationStatsCalculator.StageOffer).Count.ShouldBe(2);     // OfferReceived+
        Stage(dto, ApplicationStatsCalculator.StageAccepted).Count.ShouldBe(1);  // Accepted only

        // Monotonic non-increasing down the funnel.
        var counts = dto.Funnel.Select(f => f.Count).ToList();
        counts.ShouldBe(counts.OrderByDescending(c => c).ToList());
        Stage(dto, ApplicationStatsCalculator.StageSent).PercentOfSent.ShouldBe(100);
    }

    // ---------------------------------------------------------------
    // Off-spine v1 limitation — interviewed-then-rejected counts only at Sent
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_OffFunnelTerminal_CountsOnlyAtSent_AndFlagsLimitation()
    {
        // A single Rejected application: in reality it may have interviewed before
        // being rejected, but the aggregate keeps only the current status, so v1
        // honestly credits it ONLY at "Sent" and flags the under-count.
        ApplicationStatRow[] rows = [Sent(ApplicationStatus.Rejected)];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        Stage(dto, ApplicationStatsCalculator.StageSent).Count.ShouldBe(1);
        Stage(dto, ApplicationStatsCalculator.StageResponded).Count.ShouldBe(0);
        Stage(dto, ApplicationStatsCalculator.StageInterview).Count.ShouldBe(0);
        Stage(dto, ApplicationStatsCalculator.StageAccepted).Count.ShouldBe(0);
        // The v1 under-count signal the FE turns into a footnote.
        dto.OffFunnelExitCount.ShouldBe(1);
    }

    [Fact]
    public void Calculate_NoOffFunnelExits_FlagIsZero()
    {
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Submitted),
            Sent(ApplicationStatus.Acknowledged),
            Sent(ApplicationStatus.Accepted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.OffFunnelExitCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Percent rounding — away-from-zero, denominator-0 guard
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_Percent_RoundsAwayFromZero()
    {
        // 1 of 3 = 33.33 → 33; the other two are Submitted (sent, not responded).
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Acknowledged),
            Sent(ApplicationStatus.Submitted),
            Sent(ApplicationStatus.Submitted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.ResponseRate.Percent.ShouldBe(33);
    }

    [Fact]
    public void Calculate_Percent_TwoThirds_RoundsTo67()
    {
        ApplicationStatRow[] rows =
        [
            Sent(ApplicationStatus.Acknowledged),
            Sent(ApplicationStatus.Acknowledged),
            Sent(ApplicationStatus.Submitted),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.ResponseRate.Percent.ShouldBe(67); // 66.67 away-from-zero → 67
    }

    // ---------------------------------------------------------------
    // Monthly series — bucketed on AppliedAt, last 12 months, zero-filled
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_MonthlySeries_HasTwelveBucketsOldestToNewestEndingCurrentMonth()
    {
        var dto = ApplicationStatsCalculator.Calculate([], Now);

        dto.MonthlyApplications.Count.ShouldBe(12);
        // Oldest = July 2025, newest = June 2026 (the current month for Now).
        dto.MonthlyApplications[0].Year.ShouldBe(2025);
        dto.MonthlyApplications[0].Month.ShouldBe(7);
        dto.MonthlyApplications[^1].Year.ShouldBe(2026);
        dto.MonthlyApplications[^1].Month.ShouldBe(6);
    }

    [Fact]
    public void Calculate_MonthlySeries_BucketsByAppliedMonth()
    {
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.Zero)),
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.Zero)),
            Row(ApplicationStatus.Rejected, new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        var june = dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 6 });
        var may = dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 5 });
        june.Count.ShouldBe(2);
        may.Count.ShouldBe(1);
    }

    [Fact]
    public void Calculate_MonthlySeries_ExcludesApplicationsBeforeWindowButTotalsStillCount()
    {
        // Applied 13 months before Now → outside the 12-month series window, but
        // still part of TotalApplications / TotalSent / status counts.
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2025, 5, 10, 8, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.TotalApplications.ShouldBe(1);
        dto.TotalSent.ShouldBe(1);
        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(0); // not in any bucket
    }

    [Fact]
    public void Calculate_MonthlySeries_DraftsNeverAppear()
    {
        ApplicationStatRow[] rows = [Draft(), Draft()];

        var dto = ApplicationStatsCalculator.Calculate(rows, Now);

        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(0);
    }
}
