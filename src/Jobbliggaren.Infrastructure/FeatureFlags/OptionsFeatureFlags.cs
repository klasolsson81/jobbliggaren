using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.FeatureFlags;

/// <summary>
/// IOptionsMonitor-baserad impl. Reagerar på Secrets Manager-uppdateringar
/// utan deploy via reloadOnChange — <c>FeatureFlagsOptions.RegistrationsOpen</c>
/// kan toggle:as live när Klas öppnar/stänger pulser.
/// </summary>
public sealed class OptionsFeatureFlags(IOptionsMonitor<FeatureFlagsOptions> monitor)
    : IFeatureFlags
{
    public bool RegistrationsOpen => monitor.CurrentValue.RegistrationsOpen;
}
