using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jobbliggaren.Api.IntegrationTests.FeatureFlags;

/// <summary>
/// Test-stub som returnerar fast värde för <see cref="IFeatureFlags"/>.
/// Används av kill-switch-test för att simulera stängd registrering utan
/// att räkrasa app-startup eller IOptionsMonitor-binding.
/// </summary>
internal sealed class FixedFeatureFlags(bool registrationsOpen) : IFeatureFlags
{
    public bool RegistrationsOpen { get; } = registrationsOpen;
}

/// <summary>
/// Factory-helper som ersätter <see cref="IFeatureFlags"/>-registreringen
/// med en stub. Använd via <c>factory.WithFeatureFlags(false)</c> i tester
/// som behöver simulera kill-switch.
/// </summary>
internal static class FeatureFlagsTestExtensions
{
    public static WebApplicationFactory<Program> WithFeatureFlags(
        this ApiFactory factory, bool registrationsOpen)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFeatureFlags>();
                services.AddSingleton<IFeatureFlags>(new FixedFeatureFlags(registrationsOpen));
            });
        });
    }
}
