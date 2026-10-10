namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// The Backup card's data as the browser receives it (#1982, ADR 0157): a closed union and nothing else.
/// No path, no systemd text, no error message and no run-start time reaches this type. A test pins the
/// serialised key set.
/// </summary>
public sealed record BackupStatusDto
{
    private BackupStatusDto(
        BackupStatus status,
        BackupStatusReason? reason,
        DateTimeOffset? observedAt,
        bool? stale,
        BackupLastSuccessDto? lastSuccess,
        BackupTimerDto? timer)
    {
        Status = status;
        Reason = reason;
        ObservedAt = observedAt;
        Stale = stale;
        LastSuccess = lastSuccess;
        Timer = timer;
    }

    public BackupStatus Status { get; }

    /// <summary>Present exactly when <see cref="Status"/> is not <see cref="BackupStatus.Observed"/>.</summary>
    public BackupStatusReason? Reason { get; }

    /// <summary>
    /// The host's own clock reading when it sampled: never the time of this API call, so an old
    /// observation stays old. Absent when there is no sample, and when the sample is dated more than a minute after now.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>The observation is older than five minutes against the API's clock, which is the host's clock.</summary>
    public bool? Stale { get; }

    public BackupLastSuccessDto? LastSuccess { get; }

    public BackupTimerDto? Timer { get; }

    public static BackupStatusDto Observed(
        DateTimeOffset observedAt, bool stale, BackupLastSuccessDto lastSuccess, BackupTimerDto timer) =>
        new(BackupStatus.Observed, null, observedAt, stale, lastSuccess, timer);

    public static BackupStatusDto NotObserved(BackupStatusReason reason) =>
        new(BackupStatus.NotObserved, reason, null, null, null, null);

    public static BackupStatusDto Failed(BackupStatusReason reason, DateTimeOffset? observedAt = null) =>
        new(BackupStatus.Failed, reason, observedAt, null, null, null);
}

/// <summary>The last successful run, as a closed union: a state with a time carries it, a state without one carries none.</summary>
public sealed record BackupLastSuccessDto
{
    private BackupLastSuccessDto(BackupLastSuccessState state, DateTimeOffset? completedAt, bool? overdue)
    {
        State = state;
        CompletedAt = completedAt;
        Overdue = overdue;
    }

    /// <summary>What the stamp says.</summary>
    public BackupLastSuccessState State { get; }

    /// <summary>The end of the last successful run. Present only when <see cref="State"/> is Recorded.</summary>
    public DateTimeOffset? CompletedAt { get; }

    /// <summary>
    /// The last known success is older than the backup's own 26 h threshold, measured against the API's clock
    /// and not against the sample: a sampler that stopped must not leave the answer at "not overdue" for ever.
    /// </summary>
    public bool? Overdue { get; }

    public static BackupLastSuccessDto Recorded(DateTimeOffset completedAt, bool overdue) =>
        new(BackupLastSuccessState.Recorded, completedAt, overdue);

    public static BackupLastSuccessDto NotRecorded(BackupLastSuccessState state) =>
        state == BackupLastSuccessState.Recorded
            ? throw new ArgumentException("A recorded run has its time.", nameof(state))
            : new(state, null, null);
}

/// <summary>The backup timer, as a closed union: a scheduled timer carries its next instant, any other state carries none.</summary>
public sealed record BackupTimerDto
{
    private BackupTimerDto(BackupTimerState state, DateTimeOffset? nextRunAt)
    {
        State = state;
        NextRunAt = nextRunAt;
    }

    /// <summary>What systemd holds for the timer.</summary>
    public BackupTimerState State { get; }

    /// <summary>The instant the timer is armed for. Present only when <see cref="State"/> is Scheduled.</summary>
    public DateTimeOffset? NextRunAt { get; }

    public static BackupTimerDto Scheduled(DateTimeOffset nextRunAt) => new(BackupTimerState.Scheduled, nextRunAt);

    public static BackupTimerDto NotScheduled(BackupTimerState state) =>
        state == BackupTimerState.Scheduled
            ? throw new ArgumentException("A scheduled timer has its next instant.", nameof(state))
            : new(state, null);
}
