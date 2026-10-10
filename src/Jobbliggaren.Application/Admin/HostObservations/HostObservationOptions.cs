using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Admin.HostObservations;

/// <summary>
/// Cadence and limits of the host sampler. Application owns the contract; the Api binds it (section
/// <c>HostObservation</c>) with <c>ValidateDataAnnotations</c> and <c>ValidateOnStart</c>. Every key has a
/// default, so no human-supplied value is needed to boot (CLAUDE.md §11, dev-boot contract). The paths the
/// probe reads are code constants, not options: a setting can be widened to a file it should not read.
/// </summary>
public sealed class HostObservationOptions : IValidatableObject
{
    public const string SectionName = "HostObservation";

    /// <summary>
    /// Seconds between samples. 30: the browser refreshes every 60 s while visible, so a reading is at
    /// most about 90 s old when shown, and a CPU window of 30 s is long enough not to be a spike.
    /// </summary>
    [Range(5, 300)]
    public int SampleIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// A reading older than this when it is read is stale. 120: four missed samples, not one slow tick.
    /// </summary>
    [Range(10, 3600)]
    public int StaleAfterSeconds { get; set; } = 120;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The longest CPU window the sampler still accepts is three intervals. A stale limit at or below it would
        // call a reading stale while it is still being reported as measurable.
        if (StaleAfterSeconds <= 3 * SampleIntervalSeconds)
        {
            yield return new ValidationResult(
                "StaleAfterSeconds must be more than three times SampleIntervalSeconds.",
                [nameof(StaleAfterSeconds)]);
        }
    }
}
