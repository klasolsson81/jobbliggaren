using Jobbliggaren.Application.Admin.Backup;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Backup;

/// <summary>
/// The evaluator is pure, so every boundary of every rule is tested to the second. "Now" is always a
/// parameter; no test reads a clock.
/// </summary>
public class BackupStatusEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 13, 41, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset Sampled = new(2026, 10, 10, 13, 41, 2, TimeSpan.Zero);
    private static readonly DateTimeOffset Completed = new(2026, 10, 10, 2, 19, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset Started = new(2026, 10, 10, 2, 15, 41, TimeSpan.Zero);
    private static readonly DateTimeOffset Next = new(2026, 10, 10, 13, 49, 41, TimeSpan.Zero);

    private static BackupSample Sample(
        BackupStampSample? stamp = null, BackupTimerSample? timer = null, DateTimeOffset? sampledAt = null) =>
        new(sampledAt ?? Sampled,
            stamp ?? new BackupStampSample.Recorded(Completed, Started),
            timer ?? new BackupTimerSample.Scheduled(Next));

    private static BackupStatusDto Evaluate(BackupSample sample, DateTimeOffset? now = null) =>
        BackupStatusEvaluator.Evaluate(BackupSampleRead.Sampled(sample), now ?? Now);

    // ---- the whole sample ----

    [Fact]
    public void Evaluate_ShouldReportTheHostsOwnClockAsObservedAt_NeverTheCallersNow()
    {
        var dto = Evaluate(Sample(), Now);

        dto.Status.ShouldBe(BackupStatus.Observed);
        dto.ObservedAt.ShouldBe(Sampled);
        dto.ObservedAt.ShouldNotBe(Now);
        dto.Reason.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldKeepAnOldObservationOld_HoweverLateItIsRead()
    {
        var threeDaysLater = Sampled.AddDays(3);

        var dto = Evaluate(Sample(), threeDaysLater);

        dto.ObservedAt.ShouldBe(Sampled);
        dto.Stale.ShouldBe(true);
    }

    [Theory]
    [InlineData(300, false)] // exactly five minutes: not yet old
    [InlineData(301, true)]
    [InlineData(0, false)]
    public void Evaluate_ShouldMarkAnObservationStale_OnlyBeyondFiveMinutes(int ageSeconds, bool stale) =>
        Evaluate(Sample(), Sampled.AddSeconds(ageSeconds)).Stale.ShouldBe(stale);

    [Theory]
    [InlineData(60, false)] // within the skew tolerance
    [InlineData(61, true)]
    public void Evaluate_ShouldRefuseASampleDatedAfterNow_BeyondTheSkewTolerance(int secondsAhead, bool refused)
    {
        var dto = Evaluate(Sample(sampledAt: Now.AddSeconds(secondsAhead)));

        dto.Status.ShouldBe(refused ? BackupStatus.Failed : BackupStatus.Observed);
        if (refused)
        {
            dto.Reason.ShouldBe(BackupStatusReason.FutureSample);
            dto.ObservedAt.ShouldBeNull("a future time must not be shown as when the host looked");
            dto.LastSuccess.ShouldBeNull();
            dto.Timer.ShouldBeNull();
        }
    }

    // ---- no sample ----

    [Theory]
    [InlineData(BackupStatusReason.NotConfigured)]
    [InlineData(BackupStatusReason.NotSampledYet)]
    public void Evaluate_ShouldReportNotObserved_WithoutAnyOtherField(BackupStatusReason reason)
    {
        var dto = BackupStatusEvaluator.Evaluate(BackupSampleRead.NotObserved(reason), Now);

        dto.Status.ShouldBe(BackupStatus.NotObserved);
        dto.Reason.ShouldBe(reason);
        dto.ObservedAt.ShouldBeNull();
        dto.Stale.ShouldBeNull();
        dto.LastSuccess.ShouldBeNull();
        dto.Timer.ShouldBeNull();
    }

    [Theory]
    [InlineData(BackupStatusReason.Unreadable)]
    [InlineData(BackupStatusReason.NotARegularFile)]
    [InlineData(BackupStatusReason.TooLarge)]
    [InlineData(BackupStatusReason.InvalidFormat)]
    [InlineData(BackupStatusReason.SamplerError)]
    public void Evaluate_ShouldReportFailed_ForEveryReasonTheReaderCanGive(BackupStatusReason reason)
    {
        var dto = BackupStatusEvaluator.Evaluate(BackupSampleRead.Failed(reason), Now);

        dto.Status.ShouldBe(BackupStatus.Failed);
        dto.Reason.ShouldBe(reason);
        dto.LastSuccess.ShouldBeNull("an observation that cannot be trusted carries no facts");
    }

    [Fact]
    public void Evaluate_ShouldShowTheSamplersOwnTime_WhenItReportedAnErrorAboutItself()
    {
        var dto = BackupStatusEvaluator.Evaluate(BackupSampleRead.Failed(BackupStatusReason.SamplerError, Sampled), Now);

        dto.ObservedAt.ShouldBe(Sampled);
    }

    [Fact]
    public void Evaluate_ShouldNotShowASamplerErrorTime_ThatIsDatedAfterNow()
    {
        var dto = BackupStatusEvaluator.Evaluate(
            BackupSampleRead.Failed(BackupStatusReason.SamplerError, Now.AddMinutes(10)), Now);

        dto.ObservedAt.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldTieEveryReasonToExactlyOneStatus()
    {
        var notObserved = new[] { BackupStatusReason.NotConfigured, BackupStatusReason.NotSampledYet };

        // FutureSample is the evaluator's own verdict on a sample that parsed, so no read carries it.
        foreach (var reason in Enum.GetValues<BackupStatusReason>().Where(reason => reason != BackupStatusReason.FutureSample))
        {
            var read = notObserved.Contains(reason) ? BackupSampleRead.NotObserved(reason) : BackupSampleRead.Failed(reason);
            var dto = BackupStatusEvaluator.Evaluate(read, Now);

            dto.Status.ShouldBe(notObserved.Contains(reason) ? BackupStatus.NotObserved : BackupStatus.Failed, reason.ToString());
            dto.Reason.ShouldBe(reason);
        }
    }

    // ---- the last successful run ----

    [Fact]
    public void Evaluate_ShouldReportTheCompletionTime_NotTheStart()
    {
        var dto = Evaluate(Sample());

        dto.LastSuccess.ShouldNotBeNull();
        dto.LastSuccess.State.ShouldBe(BackupLastSuccessState.Recorded);
        dto.LastSuccess.CompletedAt.ShouldBe(Completed);
        dto.LastSuccess.Overdue.ShouldBe(false);
    }

    [Theory]
    [InlineData(26 * 3600, false)] // exactly the threshold of the backup's own --check
    [InlineData(26 * 3600 + 1, true)]
    [InlineData(25 * 3600, false)]
    public void Evaluate_ShouldCallARunOverdue_OnlyBeyondTwentySixHoursAgainstNow(int ageSeconds, bool overdue) =>
        Evaluate(Sample(), Completed.AddSeconds(ageSeconds)).LastSuccess!.Overdue.ShouldBe(overdue);

    [Fact]
    public void Evaluate_ShouldCallARunOverdue_WhenTheSamplerHasStopped_AndTheLastKnownRunIsOld()
    {
        // The measurement is the API's clock, not the sample's: a sample taken three days ago that
        // saw a run two days before it must not keep answering "not overdue".
        var sampled = Completed.AddDays(2);

        var dto = Evaluate(Sample(sampledAt: sampled), sampled.AddDays(3));

        dto.Stale.ShouldBe(true);
        dto.LastSuccess!.Overdue.ShouldBe(true);
    }

    [Theory]
    [InlineData(BackupLastSuccessState.Missing)]
    [InlineData(BackupLastSuccessState.Unreadable)]
    [InlineData(BackupLastSuccessState.Invalid)]
    public void Evaluate_ShouldPassThroughAStateWithNoRun_AndInventNoTime(BackupLastSuccessState state)
    {
        var dto = Evaluate(Sample(stamp: new BackupStampSample.NotRecorded(state)));

        dto.LastSuccess!.State.ShouldBe(state);
        dto.LastSuccess.CompletedAt.ShouldBeNull();
        dto.LastSuccess.Overdue.ShouldBeNull();
    }

    [Theory]
    [InlineData(60, true)] // a stamp a minute after the sample is clock noise
    [InlineData(61, false)]
    public void Evaluate_ShouldRefuseAStampDatedAfterTheSample_BeyondTheSkewTolerance(int secondsAfterSample, bool accepted)
    {
        var completed = Sampled.AddSeconds(secondsAfterSample);

        var stamp = Evaluate(Sample(stamp: new BackupStampSample.Recorded(completed, completed.AddMinutes(-4)))).LastSuccess!;

        stamp.State.ShouldBe(accepted ? BackupLastSuccessState.Recorded : BackupLastSuccessState.Invalid);
        if (!accepted)
        {
            stamp.CompletedAt.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(7200, true)] // two hours is the longest a run may take
    [InlineData(7201, false)]
    [InlineData(0, true)]
    [InlineData(-1, false)] // started after it completed
    public void Evaluate_ShouldRefuseAStamp_WhoseStartAndEndAreNotARun(int spanSeconds, bool accepted)
    {
        var stamp = Evaluate(Sample(stamp: new BackupStampSample.Recorded(
            Completed, Completed.AddSeconds(-spanSeconds)))).LastSuccess!;

        stamp.State.ShouldBe(accepted ? BackupLastSuccessState.Recorded : BackupLastSuccessState.Invalid);
    }

    [Theory]
    [InlineData("2020-01-01T00:00:00Z", true)]
    [InlineData("2019-12-31T23:59:59Z", false)]
    [InlineData("1970-01-01T00:00:00Z", false)]
    public void Evaluate_ShouldRefuseAStampThatPredatesTheSystem(string completedAt, bool accepted)
    {
        var completed = DateTimeOffset.Parse(completedAt, System.Globalization.CultureInfo.InvariantCulture);

        var stamp = Evaluate(Sample(stamp: new BackupStampSample.Recorded(completed, completed.AddMinutes(-4)))).LastSuccess!;

        stamp.State.ShouldBe(accepted ? BackupLastSuccessState.Recorded : BackupLastSuccessState.Invalid);
    }

    // ---- the timer ----

    [Theory]
    [InlineData(-120, true)] // elapsing right now
    [InlineData(-121, false)]
    [InlineData(366 * 86400, true)]
    [InlineData(366 * 86400 + 1, false)]
    public void Evaluate_ShouldTrustANextRun_OnlyInsideItsPlausibleWindow(int secondsFromSample, bool accepted)
    {
        var next = Sampled.AddSeconds(secondsFromSample);

        var timer = Evaluate(Sample(timer: new BackupTimerSample.Scheduled(next))).Timer!;

        timer.State.ShouldBe(accepted ? BackupTimerState.Scheduled : BackupTimerState.Unknown);
        timer.NextRunAt.ShouldBe(accepted ? next : null);
    }

    [Theory]
    [InlineData(BackupTimerState.Inactive)]
    [InlineData(BackupTimerState.NotInstalled)]
    [InlineData(BackupTimerState.Unknown)]
    public void Evaluate_ShouldPassThroughATimerWithNoNextRun_AndInventNoTime(BackupTimerState state)
    {
        var timer = Evaluate(Sample(timer: new BackupTimerSample.NotScheduled(state))).Timer!;

        timer.State.ShouldBe(state);
        timer.NextRunAt.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_ShouldJudgeEachRowOnItsOwn_SoOneBadRowLeavesTheOtherStanding()
    {
        var dto = Evaluate(Sample(
            stamp: new BackupStampSample.Recorded(Completed, Completed.AddHours(5)),
            timer: new BackupTimerSample.Scheduled(Next)));

        dto.LastSuccess!.State.ShouldBe(BackupLastSuccessState.Invalid);
        dto.Timer!.State.ShouldBe(BackupTimerState.Scheduled);
        dto.Timer.NextRunAt.ShouldBe(Next);
    }

    // ---- the sampler's own time, when it fails ----

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Evaluate_ShouldShowASamplerErrorTime_OnlyUpToTheSkewToleranceAfterNow(int secondsAfterNow, bool shown)
    {
        var dto = BackupStatusEvaluator.Evaluate(
            BackupSampleRead.Failed(BackupStatusReason.SamplerError, Now.AddSeconds(secondsAfterNow)), Now);

        dto.ObservedAt.ShouldBe(shown ? Now.AddSeconds(secondsAfterNow) : null);
    }

    // ---- the unions cannot be built wrong ----

    [Fact]
    public void Factories_ShouldRefuseTheCombinationsNoSourceProduces()
    {
        Should.Throw<ArgumentException>(() => new BackupStampSample.NotRecorded(BackupLastSuccessState.Recorded));
        Should.Throw<ArgumentException>(() => new BackupTimerSample.NotScheduled(BackupTimerState.Scheduled));
        Should.Throw<ArgumentException>(() => BackupSampleRead.Failed(BackupStatusReason.FutureSample));
        Should.Throw<ArgumentException>(() => BackupLastSuccessDto.NotRecorded(BackupLastSuccessState.Recorded));
        Should.Throw<ArgumentException>(() => BackupTimerDto.NotScheduled(BackupTimerState.Scheduled));
    }
}
