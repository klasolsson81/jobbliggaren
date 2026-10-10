using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// Cadence and limits of the services sampler. Application owns the contract; the Api binds it (section
/// <c>ServiceObservation</c>) with <c>ValidateDataAnnotations</c> and <c>ValidateOnStart</c>. Every key has a
/// default, so no human-supplied value is needed to boot (CLAUDE.md §11, dev-boot contract). The verdict
/// thresholds are <see cref="ServiceObservationLimits"/>, not options: they are tied to the jobs' own cadence by
/// a test, and a setting can be made to disagree with the job it describes.
/// </summary>
public sealed class ServiceObservationOptions : IValidatableObject
{
    public const string SectionName = "ServiceObservation";

    /// <summary>Seconds between samples. 30, like the host sampler: the overview refreshes every 60 s while visible.</summary>
    [Range(5, 300)]
    public int SampleIntervalSeconds { get; set; } = 30;

    /// <summary>A sample older than this when it is read is not judged at all: its row is Unknown.</summary>
    [Range(10, 3600)]
    public int StaleAfterSeconds { get; set; } = 120;

    /// <summary>
    /// The longest one source may take inside a tick. 5: the volatile Redis check bounds itself at 1.5 s and
    /// the audit read is one indexed range scan, so this only ever catches a hung connection.
    /// </summary>
    [Range(1, 30)]
    public int ProbeTimeoutSeconds { get; set; } = 5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Three missed ticks is a stopped sampler; one slow tick is not. A limit at or below that would call
        // a healthy sampler stale.
        if (StaleAfterSeconds <= 3 * SampleIntervalSeconds)
        {
            yield return new ValidationResult(
                "StaleAfterSeconds must be more than three times SampleIntervalSeconds.",
                [nameof(StaleAfterSeconds)]);
        }
    }
}

/// <summary>The thresholds behind the verdicts. The wire carries them as numbers, so the page words them from the API's own values.</summary>
public static class ServiceObservationLimits
{
    /// <summary>
    /// Alive when the newest heartbeat is at most this old. Hangfire's default heartbeat is 30 s; this is that
    /// plus one sampling interval, doubled, and well under its 5-minute server timeout. A test pins the
    /// default heartbeat and that the Worker sets none of its own.
    /// </summary>
    public static readonly TimeSpan WorkerAliveWithin = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The stream job runs every ten minutes (<c>*/10 * * * *</c>). Overdue is two missed runs plus five
    /// minutes. A test pins this to the registered cron.
    /// </summary>
    public static readonly TimeSpan ImportOverdueAfter = TimeSpan.FromMinutes(25);

    /// <summary>How far back the audit log is asked for a stream run. Older than this is "none recorded".</summary>
    public static readonly TimeSpan ImportWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// A sample or heartbeat dated later than the API's own clock by more than this was stamped by a clock
    /// that has since stepped, and is not an observation.
    /// </summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(5);
}
