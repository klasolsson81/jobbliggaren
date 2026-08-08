using Amazon.SimpleEmailV2;
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
/// ADR 0124 / #1237 — DI-gate for the <c>Email:Provider="Ses"</c> branch in
/// <see cref="DependencyInjection.AddEmailSender"/>. Successor to the deleted
/// <c>ResendEmailProviderGateTests</c>. Pure <see cref="ServiceCollection"/> inspection: no host
/// boot, no Testcontainers, no AWS client is ever constructed (the SDK client sits behind a factory
/// lambda that nothing here resolves), so this suite is offline and credential-free.
///
/// <para>
/// <b>Invariants pinned:</b>
/// <list type="bullet">
///   <item><b>Every missing or blank SES setting fails LOUD at REGISTRATION</b> — region, access key
///     id, secret access key, each independently. The check is deliberately a raw
///     <see cref="IConfiguration"/> read in the arm rather than a constructor guard in
///     <see cref="SesEmailSender"/>, because <c>AddSingleton&lt;TService, TImpl&gt;</c> is LAZY: a
///     constructor guard would let production boot clean and fail on the FIRST EMAIL instead. A
///     silent no-op that looks like it sends is the failure mode this arm exists to prevent.</item>
///   <item><b>A complete config resolves <see cref="IEmailSender"/> to
///     <see cref="SesEmailSender"/></b>, in Development and in Production alike — the provider key,
///     not the environment, is what selects this arm (unlike the Console arm).</item>
///   <item><b>The SDK client is registered</b>, so the sender's dependency is satisfiable.</item>
///   <item><b><see cref="IEmailSender"/> is registered EXACTLY ONCE.</b> Asserted as a COUNT, not
///     via <c>.Single()</c> not throwing: a second registration would silently win by
///     last-wins resolution while every <c>.Single()</c>-based helper in the file threw a confusing
///     sequence error instead of naming the duplicate.</item>
///   <item><b>Both registrations are Singleton</b> (senior-cto-advisor bind 3, 2026-08-08). The AWS
///     SDK client is thread-safe and owns its own pooled handler, and AWS documents one long-lived
///     client per service/region/credential pair — a Transient would build a whole SDK client per
///     email. The Resend arm's Transient reasoning does NOT transfer: <c>IResend</c> was an
///     <c>IHttpClientFactory</c> typed client, so a singleton would have frozen
///     <c>HttpMessageHandler</c> rotation. Nothing here is a typed client.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>On the credential fixtures.</b> The arm's only test of a credential is
/// <c>IsNullOrWhiteSpace</c>, so any non-blank string exercises it identically. These are
/// deliberately NOT shaped like a real AWS key (no <c>AKIA…</c> prefix): a realistic shape would buy
/// nothing behaviourally and would hand the pre-push gitleaks scan a plausible false positive
/// forever. The region IS the real one (<c>eu-north-1</c>, ADR 0124), and that matters more than it
/// looks: the arm resolves the region eagerly at registration and now REJECTS a name the SDK does
/// not know, so a made-up region exercises a different path — the one
/// <see cref="SesProvider_WithAnUnknownRegion_FailsLoud"/> owns.
/// </para>
/// </summary>
public class SesEmailProviderGateTests
{
    private const string Region = "eu-north-1";
    private const string AccessKeyId = "test-access-key-id";
    private const string SecretAccessKey = "test-secret-access-key";

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

    /// <summary>A complete, valid SES configuration — the baseline each negative case removes ONE key from.</summary>
    private static Dictionary<string, string?> FullSesSettings(string provider = "Ses") =>
        new()
        {
            [$"{EmailOptions.SectionName}:Provider"] = provider,
            [$"{SesEmailOptions.SectionName}:Region"] = Region,
            [$"{SesEmailOptions.SectionName}:AccessKeyId"] = AccessKeyId,
            [$"{SesEmailOptions.SectionName}:SecretAccessKey"] = SecretAccessKey,
        };

    private static Dictionary<string, string?> SesSettingsWith(string key, string? value)
    {
        var settings = FullSesSettings();
        if (value is null)
            settings.Remove($"{SesEmailOptions.SectionName}:{key}");
        else
            settings[$"{SesEmailOptions.SectionName}:{key}"] = value;
        return settings;
    }

    private static Type? ResolveEmailSenderImpl(
        string environmentName, IReadOnlyDictionary<string, string?> emailSettings) =>
        BuildServices(environmentName, emailSettings)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    // --- Fail loud: region ---

    [Theory]
    [InlineData(null)]   // key absent entirely
    [InlineData("")]     // present but empty
    [InlineData("   ")]  // present but whitespace
    public void SesProvider_WithoutARegion_FailsLoud(string? region)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", SesSettingsWith(nameof(SesEmailOptions.Region), region)));

        // Substring only — the guidance sentence is Swedish UI-adjacent prose and may be reworded.
        // What must not drift is WHICH setting the message names, because that is what makes the
        // crash actionable at 02:00.
        ex.Message.ShouldContain("Email:Ses:Region");
    }

    /// <summary>
    /// A region name that is well-formed but WRONG must fail at registration too, and this is a
    /// separate invariant from "blank" — it was measured fail-OPEN before ADR 0124's repair.
    /// <para>
    /// <c>RegionEndpoint.GetBySystemName</c> does not throw on an unknown name; it SYNTHESISES an
    /// endpoint. Measured 2026-08-08: <c>eu-nrth-1</c> resolved to
    /// <c>SystemName='eu-nrth-1' DisplayName='Unknown'</c> and passed the null check, the
    /// <c>[Required]</c> attribute and the endpoint construction alike. Nothing failed until the
    /// first real send, in production, after a deploy.
    /// </para>
    /// <para>
    /// The <c>us-east-1</c> row is deliberately a CONTROL, not a policy assertion: it is a real
    /// region and therefore must still PASS here. Whether a non-EU region should be refused as a
    /// data-residency guard is `security-auditor`'s call under the open Chapter V question (#1169),
    /// and pinning a policy this suite has not been given would pre-empt her.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("eu-nrth-1")]      // the realistic typo
    [InlineData("totally-bogus")]  // not region-shaped at all
    public void SesProvider_WithAnUnknownRegion_FailsLoud(string region)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", SesSettingsWith(nameof(SesEmailOptions.Region), region)));

        ex.Message.ShouldContain(region);
    }

    [Fact]
    public void SesProvider_WithAKnownNonEuRegion_StillRegisters_BecauseResidencyPolicyIsNotDecidedHere()
    {
        var impl = ResolveEmailSenderImpl(
            "Development", SesSettingsWith(nameof(SesEmailOptions.Region), "us-east-1"));

        impl.ShouldBe(typeof(SesEmailSender));
    }

    // --- Fail loud: credentials (each half independently) ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SesProvider_WithoutAnAccessKeyId_FailsLoud(string? accessKeyId)
    {
        // The Region IS supplied here, so this can only fail on the credential guard — otherwise the
        // test would pass on the region guard and prove nothing about credentials.
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices(
                "Development",
                SesSettingsWith(nameof(SesEmailOptions.AccessKeyId), accessKeyId)));

        ex.Message.ShouldContain("Email:Ses:AccessKeyId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SesProvider_WithoutASecretAccessKey_FailsLoud(string? secretAccessKey)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices(
                "Development",
                SesSettingsWith(nameof(SesEmailOptions.SecretAccessKey), secretAccessKey)));

        ex.Message.ShouldContain("Email:Ses:SecretAccessKey");
    }

    [Fact]
    public void SesProvider_WithNoSesSectionAtAll_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices(
            "Production",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Ses",
            }));

    // --- Happy path: the arm registers the SES sender ---

    // Unlike the Console arm, this one is environment-INDEPENDENT: the provider key alone selects
    // it. Pinned across all four environment names so a future "Ses only outside dev" gate cannot
    // be added without this failing.
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void SesProvider_WithFullConfiguration_RegistersSesEmailSender(string env) =>
        ResolveEmailSenderImpl(env, FullSesSettings()).ShouldBe(typeof(SesEmailSender));

    [Theory]
    [InlineData("ses")]
    [InlineData("SES")]
    [InlineData("sEs")]
    public void SesProvider_IsCaseInsensitive(string provider) =>
        ResolveEmailSenderImpl("Development", FullSesSettings(provider))
            .ShouldBe(typeof(SesEmailSender));

    [Fact]
    public void SesProvider_WithFullConfiguration_RegistersTheSesSdkClient()
    {
        // SesEmailSender depends on IAmazonSimpleEmailServiceV2 — proves the SDK got registered, not
        // just the sender. The descriptor carries a factory (ImplementationType is null), which is
        // why this asserts on ServiceType rather than on an implementation type.
        var services = BuildServices("Production", FullSesSettings());

        services.ShouldContain(d => d.ServiceType == typeof(IAmazonSimpleEmailServiceV2));
    }

    // --- Exactly one IEmailSender, and both registrations are Singleton ---

    [Fact]
    public void SesProvider_WithFullConfiguration_RegistersExactlyOneEmailSender()
    {
        var services = BuildServices("Production", FullSesSettings());

        // COUNT, not .Single(). A duplicate registration resolves last-wins and would silently
        // shadow the SES sender; .Single() would throw a sequence error that names nothing.
        services.Count(d => d.ServiceType == typeof(IEmailSender)).ShouldBe(1);
    }

    [Fact]
    public void SesProvider_WithFullConfiguration_RegistersTheEmailSenderAsASingleton()
    {
        var services = BuildServices("Production", FullSesSettings());

        var descriptor = services.Single(d => d.ServiceType == typeof(IEmailSender));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        // ImplementationType (not a factory lambda) is load-bearing for the assertions above:
        // ServiceDescriptor.ImplementationType is NULL for a factory registration, so switching to
        // one would fail every impl-type fact in this file in a way that reads like a DI bug.
        descriptor.ImplementationType.ShouldBe(typeof(SesEmailSender));
    }

    [Fact]
    public void SesProvider_WithFullConfiguration_RegistersTheSdkClientAsASingleton()
    {
        var services = BuildServices("Production", FullSesSettings());

        // AWS documents one long-lived client per service/region/credential pair; the client is
        // thread-safe and owns its own pooled handler. A Transient would construct a whole SDK
        // client per email (senior-cto-advisor bind 3, 2026-08-08).
        var descriptor = services.Single(d => d.ServiceType == typeof(IAmazonSimpleEmailServiceV2));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void SesProvider_WithFullConfiguration_RegistersExactlyOneSdkClient() =>
        BuildServices("Production", FullSesSettings())
            .Count(d => d.ServiceType == typeof(IAmazonSimpleEmailServiceV2))
            .ShouldBe(1);

    // --- Regression: the Console/Null gate is untouched by the SES arm ---

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

    [Fact]
    public void ConsoleProvider_WithSesCredentialsPresent_StillRegistersConsoleEmailSender()
    {
        // The SES keys being PRESENT must not select the SES arm — only Email:Provider does. This is
        // the state a dev box lands in the moment someone pastes prod credentials into
        // appsettings.Local.json to test something and leaves Provider on its default.
        var settings = FullSesSettings(provider: "Console");

        ResolveEmailSenderImpl("Development", settings).ShouldBe(typeof(ConsoleEmailSender));
        BuildServices("Development", settings)
            .ShouldNotContain(d => d.ServiceType == typeof(IAmazonSimpleEmailServiceV2));
    }
}
