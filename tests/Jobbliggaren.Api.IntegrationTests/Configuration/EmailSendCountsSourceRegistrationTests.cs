using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Email.Counting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1981 PR 3 (ADR 0180) — which read of our own send counts the Api composes. It follows the sender's provider switch
/// alone: only the Scaleway arm counts, so only there is there anything to read, and the read key plays no part.
/// </summary>
public sealed class EmailSendCountsSourceRegistrationTests
{
    private static Dictionary<string, string?> Settings(string? provider, string? readKey = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{ScalewayEmailOptions.SectionName}:Region"] = "fr-par",
            [$"{ScalewayEmailOptions.SectionName}:SecretKey"] = "test-scaleway-secret-key",
            [$"{ScalewayEmailOptions.SectionName}:ProjectId"] = "11111111-2222-3333-4444-555555555555",
        };
        if (provider is not null)
            settings[$"{EmailOptions.SectionName}:Provider"] = provider;
        if (readKey is not null)
            settings["Email:ScalewayRead:SecretKey"] = readKey;
        return settings;
    }

    private static ServiceCollection Compose(IReadOnlyDictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddEmailSendCountsSource(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    private static ServiceDescriptor TheSource(IReadOnlyDictionary<string, string?> settings) =>
        Compose(settings).Single(d => d.ServiceType == typeof(IEmailSendCountsSource));

    [Theory]
    [InlineData("Scaleway", typeof(SqlEmailSendCountsSource))]
    [InlineData("Console", typeof(NotConfiguredEmailSendCounts))]
    [InlineData(null, typeof(NotConfiguredEmailSendCounts))]
    public void OnlyTheProviderDecides_WhichReadIsComposed(string? provider, Type expected)
    {
        var descriptor = TheSource(Settings(provider));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        (descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType()).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test-scaleway-read-key")]
    public void TheReadKey_PlaysNoPart(string? readKey)
        => TheSource(Settings("Scaleway", readKey)).ImplementationType.ShouldBe(typeof(SqlEmailSendCountsSource));

    [Fact]
    public void TheSqlRead_ResolvesWithScopeAndBuildValidation()
    {
        using var provider = Compose(Settings("Scaleway"))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<IEmailSendCountsSource>().ShouldBeOfType<SqlEmailSendCountsSource>();
    }

    [Fact]
    public void TheWorkersPath_AddEmailSenderAlone_NeverComposesTheRead()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var services = new ServiceCollection();

        services.AddEmailSender(new ConfigurationBuilder().AddInMemoryCollection(Settings("Scaleway")).Build(), environment);

        services.ShouldNotContain(d => d.ServiceType == typeof(IEmailSendCountsSource));
    }
}
