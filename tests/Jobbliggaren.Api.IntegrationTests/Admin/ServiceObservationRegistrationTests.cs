using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The services sampler's composition (#1982): every key has a default, a bad value stops the API at start instead of
/// producing a sampler that calls every row stale, and everything is a singleton so that <c>ValidateScopes</c> has
/// nothing to refuse. No web host: only the registration, with the two ports it does not compose (the SQL source, which
/// <c>AddInfrastructure</c> registers, and Hangfire's <c>JobStorage</c>) given stand-ins.
/// </summary>
public sealed class ServiceObservationRegistrationTests
{
    private static ServiceProvider Provider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IDateTimeProvider>());
        services.AddSingleton(Substitute.For<IImportRunRecords>());
        services.AddSingleton(Substitute.For<Hangfire.JobStorage>());
        services.AddSingleton(Substitute.For<HealthCheckService>());
        services.AddServiceObservation(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void AddServiceObservation_WithNoSettingsAtAll_TheDefaultsApplyAndNothingIsRequiredToBoot()
    {
        using var provider = Provider();

        var options = provider.GetRequiredService<IOptions<ServiceObservationOptions>>().Value;

        (options.SampleIntervalSeconds, options.StaleAfterSeconds, options.ProbeTimeoutSeconds).ShouldBe((30, 120, 5));
    }

    [Fact]
    public void AddServiceObservation_ASetting_IsBoundFromTheServiceObservationSection()
    {
        using var provider = Provider(
            ("ServiceObservation:SampleIntervalSeconds", "10"),
            ("ServiceObservation:StaleAfterSeconds", "45"),
            ("ServiceObservation:ProbeTimeoutSeconds", "2"));

        var options = provider.GetRequiredService<IOptions<ServiceObservationOptions>>().Value;

        (options.SampleIntervalSeconds, options.StaleAfterSeconds, options.ProbeTimeoutSeconds).ShouldBe((10, 45, 2));
    }

    [Theory]
    [InlineData("30", "90", "5")] // not above three intervals
    [InlineData("4", "120", "5")] // under the shipped floor
    [InlineData("301", "2000", "5")] // over the shipped ceiling
    [InlineData("30", "9", "5")]
    [InlineData("30", "3601", "5")] // over the shipped ceiling of the stale limit
    [InlineData("30", "120", "0")] // a probe that may take no time
    [InlineData("30", "120", "31")] // over the shipped ceiling of the probe timeout
    public void AddServiceObservation_ABadCadence_IsRefusedAtStartAndWhenFirstRead(string interval, string stale, string probe)
    {
        using var provider = Provider(
            ("ServiceObservation:SampleIntervalSeconds", interval),
            ("ServiceObservation:StaleAfterSeconds", stale),
            ("ServiceObservation:ProbeTimeoutSeconds", probe));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ServiceObservationOptions>>().Value);
    }

    [Fact]
    public void AddServiceObservation_TheSampler_IsOneSingletonServingTheReaderAndTheTickAndTheServiceIsHosted()
    {
        using var provider = Provider();

        var sampler = provider.GetRequiredService<ServiceObservationSampler>();

        provider.GetRequiredService<IServiceObservationReader>().ShouldBeSameAs(sampler);
        provider.GetRequiredService<IServiceObservationSampler>().ShouldBeSameAs(sampler);
        provider.GetServices<IHostedService>().OfType<ServiceObservationService>().ShouldHaveSingleItem();
        provider.GetRequiredService<ILogger<ServiceObservationSampler>>().ShouldNotBeNull();
    }
}
