namespace Jobbliggaren.Infrastructure.FeatureFlags;

public sealed class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// Default false (stängd-by-default per ADR 0005 amendment).
    /// Klas sätter true när han vill öppna för pulse av klasskamrater.
    /// </summary>
    public bool RegistrationsOpen { get; init; }
}
