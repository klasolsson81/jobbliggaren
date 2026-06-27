using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// ADR 0080 Vag 4 PR-4b — DI-gate for <see cref="DependencyInjection.AddEmailSender"/>, the email
/// provider-switch that BOTH the Api (via AddInfrastructure) AND the HTTP-free Worker (ADR 0023) call
/// to register <see cref="IEmailSender"/> for the Vag 4 match-notification jobs. Pins the gate both
/// rely on: Dev/Test → Console, otherwise → Null, and a Resend provider without an ApiKey fails LOUD
/// (never a silent no-op that looks like it sends). Pure registration inspection — no host boot /
/// Testcontainers. (The Resend-with-key branch is pinned by ResendEmailProviderGateTests.)
/// </summary>
public class AddEmailSenderGateTests
{
    private static ServiceCollection BuildServices(string environmentName, string? provider = null)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var values = new Dictionary<string, string?>();
        if (provider is not null)
            values[$"{EmailOptions.SectionName}:Provider"] = provider;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddEmailSender(config, env);
        return services;
    }

    private static Type? ResolveEmailSenderImpl(string environmentName, string? provider = null) =>
        BuildServices(environmentName, provider)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void AddEmailSender_InDevelopmentOrTest_RegistersConsoleEmailSender(string env) =>
        ResolveEmailSenderImpl(env).ShouldBe(typeof(ConsoleEmailSender));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AddEmailSender_OutsideDevelopmentOrTest_FallsBackToNullEmailSender(string env) =>
        ResolveEmailSenderImpl(env).ShouldBe(typeof(NullEmailSender));

    [Fact]
    public void AddEmailSender_DefaultProviderInProduction_RegistersNullEmailSender() =>
        ResolveEmailSenderImpl("Production", provider: null).ShouldBe(typeof(NullEmailSender));

    [Fact]
    public void AddEmailSender_ResendWithoutApiKey_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices("Development", "Resend"));

    [Fact]
    public void AddEmailSender_UnknownProvider_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices("Development", "Smtp"));
}
