namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// Judges a host sample against the API's clock (#1982, ADR 0157). Pure: the clock is a parameter, so
/// every boundary is testable to the second. The reader checks syntax and holds no clock; every rule
/// that needs "now" or compares two fields lives here, in one place.
/// </summary>
public static class BackupStatusEvaluator
{
    /// <summary>
    /// A backup older than this reads as a stopped backup. Equal to <c>MAX_STAMP_AGE_SECONDS</c> in
    /// <c>jobbliggaren-backup.sh</c>, which its own <c>--check</c> uses; an architecture test compares the two.
    /// </summary>
    public static readonly TimeSpan BackupOverdueAfter = TimeSpan.FromHours(26);

    /// <summary>
    /// The sampler runs once a minute and the overview marks a source old after five minutes, so this is
    /// five missed samples. Equal to <c>OVERVIEW_STALE_MS</c> in the web app.
    /// </summary>
    public static readonly TimeSpan ObservationStaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest a run may take between its start and its stamp. The unit's own limit is
    /// <c>TimeoutStartSec=3600</c>; an architecture test keeps this above it.
    /// </summary>
    public static readonly TimeSpan MaxRunSpan = TimeSpan.FromHours(2);

    /// <summary>Slack for a clock reading taken a moment apart from another on the same box.</summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(60);

    /// <summary>A timer elapsing right now is still read as armed; systemd re-arms it within moments.</summary>
    public static readonly TimeSpan NextRunPastTolerance = TimeSpan.FromSeconds(120);

    /// <summary>The longest a nightly timer can plausibly be armed ahead.</summary>
    public static readonly TimeSpan NextRunHorizon = TimeSpan.FromDays(366);

    /// <summary>No backup stamp predates the system that writes it.</summary>
    public static readonly DateTimeOffset EarliestPlausibleInstant = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static BackupStatusDto Evaluate(BackupSampleRead read, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        if (read.Status == BackupStatus.NotObserved)
        {
            return BackupStatusDto.NotObserved(read.Reason ?? BackupStatusReason.NotSampledYet);
        }

        if (read.Sample is null)
        {
            return BackupStatusDto.Failed(read.Reason ?? BackupStatusReason.InvalidFormat, ReasonableInstant(read.SampledAt, now));
        }

        var sample = read.Sample;
        if (sample.SampledAt > now + ClockSkewTolerance)
        {
            return BackupStatusDto.Failed(BackupStatusReason.FutureSample);
        }

        return BackupStatusDto.Observed(
            sample.SampledAt,
            now - sample.SampledAt > ObservationStaleAfter,
            EvaluateLastSuccess(sample, now),
            EvaluateTimer(sample));
    }

    // An instant the sampler reported about itself while failing is shown only when it is not dated more than a minute after now.
    private static DateTimeOffset? ReasonableInstant(DateTimeOffset? instant, DateTimeOffset now) =>
        instant is { } value && value <= now + ClockSkewTolerance ? value : null;

    private static BackupLastSuccessDto EvaluateLastSuccess(BackupSample sample, DateTimeOffset now)
    {
        if (sample.LastSuccess is not BackupStampSample.Recorded recorded)
        {
            return BackupLastSuccessDto.NotRecorded(sample.LastSuccess.State);
        }

        var plausible =
            recorded.CompletedAt >= EarliestPlausibleInstant
            && recorded.CompletedAt <= sample.SampledAt + ClockSkewTolerance
            && recorded.StartedAt <= recorded.CompletedAt
            && recorded.CompletedAt - recorded.StartedAt <= MaxRunSpan;
        return plausible
            ? BackupLastSuccessDto.Recorded(recorded.CompletedAt, now - recorded.CompletedAt > BackupOverdueAfter)
            : BackupLastSuccessDto.NotRecorded(BackupLastSuccessState.Invalid);
    }

    private static BackupTimerDto EvaluateTimer(BackupSample sample)
    {
        if (sample.Timer is not BackupTimerSample.Scheduled scheduled)
        {
            return BackupTimerDto.NotScheduled(sample.Timer.State);
        }

        var plausible =
            scheduled.NextRunAt >= sample.SampledAt - NextRunPastTolerance
            && scheduled.NextRunAt <= sample.SampledAt + NextRunHorizon;
        return plausible
            ? BackupTimerDto.Scheduled(scheduled.NextRunAt)
            : BackupTimerDto.NotScheduled(BackupTimerState.Unknown);
    }
}
