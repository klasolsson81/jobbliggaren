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
    /// observation stays old. Absent when there is no sample, and when the sample is dated after now.
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

/// <param name="State">What the stamp says.</param>
/// <param name="CompletedAt">The end of the last successful run. Present only when <paramref name="State"/> is Recorded.</param>
/// <param name="Overdue">
/// The last known success is older than the backup's own 26 h threshold, measured against the API's clock
/// and not against the sample: a sampler that stopped must not leave the answer at "not overdue" for ever.
/// </param>
public sealed record BackupLastSuccessDto(BackupLastSuccessState State, DateTimeOffset? CompletedAt, bool? Overdue);

/// <param name="State">What systemd holds for the timer.</param>
/// <param name="NextRunAt">The instant the timer is armed for. Present only when <paramref name="State"/> is Scheduled.</param>
public sealed record BackupTimerDto(BackupTimerState State, DateTimeOffset? NextRunAt);
