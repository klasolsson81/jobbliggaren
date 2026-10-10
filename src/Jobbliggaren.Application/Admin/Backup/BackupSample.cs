namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// What the host sampler published about the backup, after the reader has checked the file's syntax and
/// before anything is judged against a clock. Timestamps are UTC instants; nothing here is a string a
/// browser could see.
/// </summary>
public sealed record BackupSample(DateTimeOffset SampledAt, BackupStampSample LastSuccess, BackupTimerSample Timer);

/// <summary>
/// The stamp, as a closed union: a run has both its times or it is not a run. The shapes the reader cannot
/// produce (a recorded run without a time) cannot be built, so no rule has to degrade them.
/// </summary>
public abstract record BackupStampSample
{
    private BackupStampSample()
    {
    }

    public abstract BackupLastSuccessState State { get; }

    /// <summary>A run that finished: <paramref name="CompletedAt"/> is the stamp's mtime, <paramref name="StartedAt"/> its content.</summary>
    public sealed record Recorded(DateTimeOffset CompletedAt, DateTimeOffset StartedAt) : BackupStampSample
    {
        public override BackupLastSuccessState State => BackupLastSuccessState.Recorded;
    }

    /// <summary>No run to show: the stamp is missing, unreadable or invalid.</summary>
    public sealed record NotRecorded : BackupStampSample
    {
        public NotRecorded(BackupLastSuccessState state)
        {
            if (state == BackupLastSuccessState.Recorded)
            {
                throw new ArgumentException("A recorded run is Recorded, with its times.", nameof(state));
            }

            State = state;
        }

        public override BackupLastSuccessState State { get; }
    }
}

/// <summary>The timer, as a closed union: a schedule has its next instant or it is not scheduled.</summary>
public abstract record BackupTimerSample
{
    private BackupTimerSample()
    {
    }

    public abstract BackupTimerState State { get; }

    public sealed record Scheduled(DateTimeOffset NextRunAt) : BackupTimerSample
    {
        public override BackupTimerState State => BackupTimerState.Scheduled;
    }

    public sealed record NotScheduled : BackupTimerSample
    {
        public NotScheduled(BackupTimerState state)
        {
            if (state == BackupTimerState.Scheduled)
            {
                throw new ArgumentException("A scheduled timer is Scheduled, with its next instant.", nameof(state));
            }

            State = state;
        }

        public override BackupTimerState State { get; }
    }
}

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

    /// <summary>
    /// The reader's refusals. <see cref="BackupStatusReason.FutureSample"/> is not one of them: it is the evaluator's
    /// own verdict on a sample that parsed, so no read carries it.
    /// </summary>
    public static BackupSampleRead Failed(BackupStatusReason reason, DateTimeOffset? sampledAt = null)
    {
        if (reason == BackupStatusReason.FutureSample)
        {
            throw new ArgumentException("A future sample is the evaluator's verdict, not a refusal of the reader.", nameof(reason));
        }

        return new(null, BackupStatus.Failed, reason, sampledAt);
    }
}
