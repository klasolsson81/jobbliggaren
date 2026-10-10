using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Backup;

/// <summary>
/// Whether the Backup card has an observation to show (#1982, ADR 0157). <see cref="NotObserved"/> is the
/// expected state until the host sampler is enabled and is not an error; <see cref="Failed"/> is an
/// observation that exists and cannot be trusted.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupStatus>))]
public enum BackupStatus
{
    Observed,
    NotObserved,
    Failed,
}

/// <summary>
/// Why the card has no trustworthy observation. One enum for the reader's syntax failures and the evaluator's
/// semantic ones, so the browser sees one closed set and a test can tie every reason to exactly one status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupStatusReason>))]
public enum BackupStatusReason
{
    /// <summary>The host-bridge directory is not configured: a dev run, or a deploy without the mount.</summary>
    NotConfigured,

    /// <summary>The directory is configured and holds no backup file: the sampler has not run.</summary>
    NotSampledYet,

    Unreadable,
    NotARegularFile,
    TooLarge,
    InvalidFormat,

    /// <summary>The sampler ran and said it could not establish the facts.</summary>
    SamplerError,

    /// <summary>The sample is dated more than a minute after now: a broken clock or a tampered file.</summary>
    FutureSample,
}

/// <summary>What the stamp that <c>jobbliggaren-backup.sh</c> writes last on its success path says.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupLastSuccessState>))]
public enum BackupLastSuccessState
{
    Recorded,
    Missing,
    Unreadable,
    Invalid,
}

/// <summary>What systemd holds for the backup timer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackupTimerState>))]
public enum BackupTimerState
{
    /// <summary>Active and armed for <c>NextRunAt</c>.</summary>
    Scheduled,

    /// <summary>Installed and not active: disabled or stopped. The state of the box until the backup is switched on.</summary>
    Inactive,

    NotInstalled,
    Unknown,
}
