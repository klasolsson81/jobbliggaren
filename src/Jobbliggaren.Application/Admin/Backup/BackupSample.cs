namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// What the host sampler published about the backup, after the reader has checked the file's syntax and
/// before anything is judged against a clock. Timestamps are UTC instants; nothing here is a string a
/// browser could see.
/// </summary>
public sealed record BackupSample(DateTimeOffset SampledAt, BackupStampSample LastSuccess, BackupTimerSample Timer);

public sealed record BackupStampSample(
    BackupLastSuccessState State,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? StartedAt = null);

public sealed record BackupTimerSample(BackupTimerState State, DateTimeOffset? NextRunAt = null);

/// <summary>
/// The outcome of one read of the host-bridge file: the sample, or a closed reason for having none.
/// Built through the factories so an impossible pairing (a sample with a reason) cannot exist.
/// </summary>
public sealed record BackupSampleRead
{
    private BackupSampleRead(BackupSample? sample, BackupStatus status, BackupStatusReason? reason, DateTimeOffset? sampledAt)
    {
        Sample = sample;
        Status = status;
        Reason = reason;
        SampledAt = sampledAt;
    }

    public BackupSample? Sample { get; }

    public BackupStatus Status { get; }

    public BackupStatusReason? Reason { get; }

    /// <summary>Only when the sampler itself reported an error: its own clock reading, still validated by the evaluator.</summary>
    public DateTimeOffset? SampledAt { get; }

    public static BackupSampleRead Sampled(BackupSample sample) => new(sample, BackupStatus.Observed, null, sample.SampledAt);

    public static BackupSampleRead NotObserved(BackupStatusReason reason) => new(null, BackupStatus.NotObserved, reason, null);

    public static BackupSampleRead Failed(BackupStatusReason reason, DateTimeOffset? sampledAt = null) =>
        new(null, BackupStatus.Failed, reason, sampledAt);
}
