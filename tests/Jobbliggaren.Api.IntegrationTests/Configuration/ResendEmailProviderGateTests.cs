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
/// ADR 0080 Vag 4 PR-4a — DI-gate for the <c>Email:Provider="Resend"</c> branch in
/// <see cref="DependencyInjection.AddEmailSender"/>. Pure registration inspection
/// (no host boot / Testcontainers).
///
/// Invariants pinned:
///   - Resend WITHOUT an ApiKey fails loud (InvalidOperationException) — never a silent no-op
///     that looks like it sends (security: a swallowed misconfig would drop real mail silently);
///   - Resend WITH an ApiKey resolves <see cref="IEmailSender"/> to <see cref="ResendEmailSender"/>;
///   - the existing Console gate (Dev/Test → Console, otherwise → Null) is unchanged (regression);
///   - an unknown provider still throws.
/// </summary>
public class ResendEmailProviderGateTests
{
    private static ServiceCollection BuildServices(
        string environmentName,
        IReadOnlyDictionary<string, string?> emailSettings)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(emailSettings)
            .Build();

        var services = new ServiceCollection();
        services.AddEmailSender(config, env);
        return services;
    }

    private static Type? ResolveEmailSenderImpl(
        string environmentName, IReadOnlyDictionary<string, string?> emailSettings) =>
        BuildServices(environmentName, emailSettings)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    // --- Resend: fail-loud when ApiKey missing/blank ---

    [Fact]
    public void ResendProvider_WithoutApiKey_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices(
            "Development",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Resend",
            }));

    [Fact]
    public void ResendProvider_WithBlankApiKey_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices(
            "Development",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Resend",
                [$"{EmailOptions.SectionName}:ApiKey"] = "   ",
            }));

    // --- Resend: registers ResendEmailSender when ApiKey present ---

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ResendProvider_WithApiKey_RegistersResendEmailSender(string env) =>
        ResolveEmailSenderImpl(
            env,
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Resend",
                [$"{EmailOptions.SectionName}:ApiKey"] = "re_test_key",
            })
            .ShouldBe(typeof(ResendEmailSender));

    [Fact]
    public void ResendProvider_IsCaseInsensitive() =>
        ResolveEmailSenderImpl(
            "Development",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "resend",
                [$"{EmailOptions.SectionName}:ApiKey"] = "re_test_key",
            })
            .ShouldBe(typeof(ResendEmailSender));

    [Fact]
    public void ResendProvider_WithApiKey_RegistersIResendClient()
    {
        // AddResend wires IResend as a typed HTTP client — proves the SDK got registered,
        // not just the sender. (ResendEmailSender depends on IResend.)
        var services = BuildServices(
            "Development",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Resend",
                [$"{EmailOptions.SectionName}:ApiKey"] = "re_test_key",
            });

        services.ShouldContain(d => d.ServiceType == typeof(global::Resend.IResend));
    }

    // --- Regression: the existing Console gate is unchanged ---

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void ConsoleProvider_InDevelopmentOrTest_StillRegistersConsoleEmailSender(string env) =>
        ResolveEmailSenderImpl(
            env,
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Console",
            })
            .ShouldBe(typeof(ConsoleEmailSender));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ConsoleProvider_OutsideDevelopmentOrTest_StillFallsBackToNullEmailSender(string env) =>
        ResolveEmailSenderImpl(
            env,
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Console",
            })
            .ShouldBe(typeof(NullEmailSender));

    // --- Unknown provider still throws ---

    [Fact]
    public void UnknownProvider_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices(
            "Development",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Smtp",
            }));
}
