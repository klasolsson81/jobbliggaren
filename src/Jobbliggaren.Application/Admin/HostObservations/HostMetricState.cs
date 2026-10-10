using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.HostObservations;

/// <summary>
/// What the admin overview may claim about one host reading. Exactly one applies, so a failing reading
/// never blanks the others and an unknown value is never rendered as zero (ADR 0150 D2).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HostMetricState>))]
public enum HostMetricState
{
    /// <summary>Sampled, and young enough at the time of the read.</summary>
    Available,

    /// <summary>The newest value is older than the stale limit; it keeps its original sample time.</summary>
    Stale,

    /// <summary>No baseline yet: the first CPU sample after the API started.</summary>
    Collecting,

    /// <summary>
    /// The reading is built but this deployment cannot supply it (no procfs, or a /proc that does not
    /// describe the host). Never "Kommer snart": that is ADR 0150 D2's word for a capability not built.
    /// </summary>
    NotObservable,

    /// <summary>This tick could not produce a trustworthy value.</summary>
    Failed,
}

/// <summary>
/// Why a reading has no value. It stays on the server: in the snapshot, in edge-triggered logs and in
/// tests. The browser receives the state only, never a reason, a path or an exception message.
/// </summary>
public enum HostMetricReason
{
    PlatformUnsupported,
    HostViewUnverified,
    ReadFailed,
    ParseFailed,
    Implausible,
    CounterReset,
    IntervalInvalid,
    SamplerStalled,
}
