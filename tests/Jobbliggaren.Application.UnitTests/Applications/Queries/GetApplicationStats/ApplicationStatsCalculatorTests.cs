using Jobbliggaren.Application.Applications.Queries.GetApplicationStats;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Infrastructure.Time;
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
    // The real calendar, not a stub: it is pure and deterministic, so the Swedish
    // boundary is exercised once here rather than asserted twice in two places,
    // and these tests stay discriminating under a mutation of the calendar
    // (idiom: RefreshLandingStatsJobTests).
    //
    // NOT because CLAUDE.md §5 `Tests:` forbids a stub — it does not. §5 attaches
    // the obligation to the ASSERTION, not the seam, and a stub returning 22:00Z
    // would return a value the real adapter does emit. `dotnet-architect` caught
    // that over-citation once already. This is the tighter option, not the
    // mandated one.
    private static readonly SwedishCalendar Calendar = new();

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
        var dto = ApplicationStatsCalculator.Calculate([], Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

        dto.TotalApplications.ShouldBe(3); // drafts counted in the raw total
        dto.TotalSent.ShouldBe(1);         // only the submitted one is "sent"
        CountOf(dto, ApplicationStatus.Draft).ShouldBe(2);
        CountOf(dto, ApplicationStatus.Submitted).ShouldBe(1);
    }

    [Fact]
    public void Calculate_DraftOnly_AllRatesHaveZeroDenominator()
    {
        ApplicationStatRow[] rows = [Draft()];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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
        var dto = ApplicationStatsCalculator.Calculate([], Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

        dto.ResponseRate.Percent.ShouldBe(67); // 66.67 away-from-zero → 67
    }

    // ---------------------------------------------------------------
    // Monthly series — bucketed on AppliedAt, last 12 months, zero-filled
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_MonthlySeries_HasTwelveBucketsOldestToNewestEndingCurrentMonth()
    {
        var dto = ApplicationStatsCalculator.Calculate([], Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

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

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

        dto.TotalApplications.ShouldBe(1);
        dto.TotalSent.ShouldBe(1);
        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(0); // not in any bucket
    }

    [Fact]
    public void Calculate_MonthlySeries_DraftsNeverAppear()
    {
        ApplicationStatRow[] rows = [Draft(), Draft()];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Monthly series — the SWEDISH civil month (Klas-direktiv 2026-07-28)
    //
    // Everything above this line sits mid-month in a UTC+2 month, which is why
    // none of it could tell the two calendars apart: a hardcoded -2 h offset
    // passed the whole class, and so did the retired UTC implementation. These
    // cases exist because the change would otherwise ship unmeasured — the same
    // hole the predecessor PR measured in its own suite.
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_MonthlySeries_JanuaryBucket_IsLabelledJanuary2026_NotDecember2025()
    {
        // THE test of this change, and it fails under the tempting wrong
        // implementations rather than only under the old one (CTO-bind
        // 2026-07-28-B, landing condition 1). `now` is 2025-12-31T23:30:00Z =
        // 2026-01-01 00:30 Swedish (CET), so the newest bucket must be
        // January 2026. It dies three ways:
        //
        //   1. the retired UTC anchor      → newest bucket is December 2025
        //   2. the StartOfDay(now) shortcut → December 2025 (that instant is the
        //      previous day's 23:00Z, and on the 1st that is the previous month)
        //   3. a label read off the boundary instant → December 2025, because the
        //      Swedish January opens at 2025-12-31T23:00Z
        //
        // The FE prints these two ints straight into the chart axis
        // (application-stats.tsx), so the bar would be captioned "december 2025".
        var now = new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero);
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, now);

        dto.MonthlyApplications[^1].Year.ShouldBe(2026);
        dto.MonthlyApplications[^1].Month.ShouldBe(1);
        dto.MonthlyApplications[^1].Count.ShouldBe(1);
        dto.MonthlyApplications[0].Year.ShouldBe(2025);
        dto.MonthlyApplications[0].Month.ShouldBe(2);
        // December 2025 IS in this window — it is the second-to-last bucket of a
        // series ending in January 2026 — so its presence proves nothing. What
        // matters is that it is in its own place and EMPTY: under a label taken
        // from the boundary instant the whole series slides back a month, and
        // December is what the newest bucket becomes.
        dto.MonthlyApplications[^2].Year.ShouldBe(2025);
        dto.MonthlyApplications[^2].Month.ShouldBe(12);
        dto.MonthlyApplications[^2].Count.ShouldBe(0);
        dto.MonthlyApplications.ShouldNotContain(m => m.Year == 2026 && m.Month == 2);
    }

    [Fact]
    public void Calculate_MonthlySeries_AnchorsOnTheSwedishMonth_JustAfterSwedishMidnightOnTheFirst()
    {
        // The summer twin of the case above, and the one that isolates the ANCHOR
        // from the label: 2026-07-31T22:30:00Z is 2026-08-01 00:30 Swedish (CEST).
        // The series must end in August 2026, and the row 15 minutes later must
        // land in it rather than in July.
        var now = new DateTimeOffset(2026, 7, 31, 22, 30, 0, TimeSpan.Zero);
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 7, 31, 22, 45, 0, TimeSpan.Zero)),
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, now);

        dto.MonthlyApplications[^1].Year.ShouldBe(2026);
        dto.MonthlyApplications[^1].Month.ShouldBe(8);
        dto.MonthlyApplications[0].Year.ShouldBe(2025);
        dto.MonthlyApplications[0].Month.ShouldBe(9);
        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 8 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 7 }).Count.ShouldBe(1);
    }

    [Fact]
    public void Calculate_MonthlySeries_WinterAndSummerBucketsUseDifferentBoundaries()
    {
        // One series spanning both DST polarities, so no fixed offset survives:
        // a hardcoded +2 h puts the 23:30 row of 31 December into January, and a
        // hardcoded +1 h leaves the 00:30 row of 1 June in May.
        ApplicationStatRow[] rows =
        [
            // 00:30 on 1 Jan 2026 Swedish (CET) → January 2026
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2025, 12, 31, 23, 30, 0, TimeSpan.Zero)),
            // 23:30 on 31 Dec 2025 Swedish (CET) → December 2025
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2025, 12, 31, 22, 30, 0, TimeSpan.Zero)),
            // 00:30 on 1 Jun 2026 Swedish (CEST) → June 2026
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 5, 31, 22, 30, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, Now);

        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 1 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Single(m => m is { Year: 2025, Month: 12 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 6 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 5 }).Count.ShouldBe(0);
    }

    [Fact]
    public void Calculate_MonthlySeries_ARowExactlyOnTheSwedishMonthBoundary_IsInTheOpeningMonth()
    {
        // 2026-06-30T22:00:00Z = 2026-07-01 00:00:00 Swedish, exactly. The bucket
        // predicate's `>=` was untested here: under `>` the row falls into NO
        // bucket, which is why June is asserted at zero rather than the row merely
        // being "somewhere".
        var now = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, now);

        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 7 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 6 }).Count.ShouldBe(0);
        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(1);
    }

    [Fact]
    public void Calculate_MonthlySeries_JulyRowOnThe31st_LandsInTheJulyBucket_NotDroppedByAddMonths()
    {
        // A bucket end derived as monthStart.AddMonths(1) closes the Swedish July
        // at 2026-07-30T22:00Z, a full day early. The row is then AFTER July's end
        // and BEFORE August's start, so it lands in NO bucket at all — the Sum
        // assertion is what makes that visible, since every per-bucket count would
        // simply read zero and look like an empty month.
        var now = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
        ApplicationStatRow[] rows =
        [
            Row(ApplicationStatus.Submitted, new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)),
        ];

        var dto = ApplicationStatsCalculator.Calculate(rows, Calendar, now);

        dto.MonthlyApplications.Single(m => m is { Year: 2026, Month: 7 }).Count.ShouldBe(1);
        dto.MonthlyApplications.Sum(m => m.Count).ShouldBe(1);
    }
}
