namespace Jobbliggaren.Infrastructure.Admin.HostBridge;

/// <summary>
/// Where the API finds what the host sampler publishes (#1982, ADR 0157). Deliberately no
/// <c>ValidateOnStart</c>: an unset directory is a valid state ("the bridge is not mounted") that the Backup
/// card reports as not observed, and it must not stop the only production host or a dev run. A deploy that
/// forgets the mount is caught by the compose pin, not at boot.
/// </summary>
public sealed class HostBridgeOptions
{
    public const string SectionName = "HostBridge";

    /// <summary>
    /// The configuration key; compose sets it as <c>HostBridge__Directory</c>. Built from the property's own name,
    /// so a rename of the property moves the key that the compose pin and the tests read.
    /// </summary>
    public const string DirectoryConfigKey = SectionName + ":" + nameof(Directory);

    /// <summary>An absolute path. Anything else is treated as not configured.</summary>
    public string? Directory { get; set; }
}
