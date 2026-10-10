using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The host sampler's composition (#1982): every key has a default, a bad value stops the API at start instead of
/// producing a sampler that calls every reading stale, and everything is a singleton so that
/// <c>ValidateScopes</c> has nothing to refuse. No web host: only the registration.
/// </summary>
public sealed class HostObservationRegistrationTests
{
    private static ServiceProvider Provider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddHostObservation(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void WithNoSettingsAtAll_TheDefaultsApplyAndNothingIsRequiredToBoot()
    {
        using var provider = Provider();

        var options = provider.GetRequiredService<IOptions<HostObservationOptions>>().Value;

        (options.SampleIntervalSeconds, options.StaleAfterSeconds).ShouldBe((30, 120));
    }

    [Fact]
    public void ASettingIsBoundFromTheHostObservationSection()
    {
        using var provider = Provider(
            ("HostObservation:SampleIntervalSeconds", "10"), ("HostObservation:StaleAfterSeconds", "45"));

        var options = provider.GetRequiredService<IOptions<HostObservationOptions>>().Value;

        (options.SampleIntervalSeconds, options.StaleAfterSeconds).ShouldBe((10, 45));
    }

    [Theory]
    [InlineData("30", "90")] // not above three intervals
    [InlineData("4", "120")] // under the shipped floor
    [InlineData("301", "2000")] // over the shipped ceiling
    [InlineData("30", "9")]
    public void ABadCadence_IsRefusedWhenTheOptionsAreFirstRead(string interval, string stale)
    {
        using var provider = Provider(
            ("HostObservation:SampleIntervalSeconds", interval), ("HostObservation:StaleAfterSeconds", stale));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HostObservationOptions>>().Value);
    }

    [Fact]
    public void TheSamplerIsOneSingletonServingTheReaderAndTheTick_AndTheServiceIsHosted()
    {
        using var provider = Provider();

        var sampler = provider.GetRequiredService<HostObservationSampler>();

        provider.GetRequiredService<IHostObservationReader>().ShouldBeSameAs(sampler);
        provider.GetRequiredService<IHostObservationSampler>().ShouldBeSameAs(sampler);
        provider.GetServices<IHostedService>().OfType<HostObservationService>().ShouldHaveSingleItem();
        provider.GetRequiredService<ILogger<HostObservationSampler>>().ShouldNotBeNull();
    }
}
