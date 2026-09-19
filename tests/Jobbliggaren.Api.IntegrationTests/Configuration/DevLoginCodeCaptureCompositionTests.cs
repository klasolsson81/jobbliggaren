using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// DEV-ONLY — the login-code capture's production wiring, measured on the UNSWAPPED composition
/// (security-auditor Q-S4): every integration host replaces <see cref="IEmailSender"/>, and ApiFactory adds
/// the capture back itself, so no host can see whether production code registered it. Development wraps the
/// Console sender and registers the reader; Production registers neither. The Development half is the
/// control the Production half is measured against.
/// </summary>
public sealed class DevLoginCodeCaptureCompositionTests
{
    private static ServiceProvider Compose(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDateTimeProvider>(Substitute.For<IDateTimeProvider>());
        services.AddEmailSender(new ConfigurationBuilder().Build(), environment);
        services.AddDevOnlyTestingSupport(environment);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Development_wraps_the_console_sender_and_registers_the_reader()
    {
        using var provider = Compose(Environments.Development);

        provider.GetRequiredService<IEmailSender>().ShouldBeOfType<DevLoginCodeCapturingEmailSender>();
        provider.GetRequiredService<IDevLoginCodeReader>().ShouldBeOfType<DevLoginCodeCapture>();
    }

    [Fact]
    public void Production_registers_neither_the_wrapper_nor_the_capture()
    {
        using var provider = Compose(Environments.Production);

        provider.GetRequiredService<IEmailSender>().ShouldBeOfType<NullEmailSender>();
        provider.GetService<IDevLoginCodeReader>().ShouldBeNull();
        provider.GetService<DevLoginCodeCapture>().ShouldBeNull();
    }
}
