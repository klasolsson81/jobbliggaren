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

    /// <summary>
    /// A region OUTSIDE the EEA must be refused even though it is perfectly real
    /// (security-auditor ruling 2026-08-08). <c>Email:Ses:Region</c> is the only string that decides
    /// the jurisdiction of every outgoing PII transfer, and <c>us-east-1</c> is the default in
    /// practically every AWS example a human might copy.
    /// <para>
    /// <b>The last two rows are the ones that matter</b>, and they are why the guard is an explicit
    /// list rather than a <c>StartsWith("eu-")</c> check: London is the UK and Zurich is
    /// Switzerland — both third countries, both <c>eu-</c>-prefixed. A prefix guard would have
    /// admitted them while looking exactly as strict.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("us-east-1")]      // the copy-paste default
    [InlineData("eu-west-2")]      // Europe (London) — UK, NOT EEA
    [InlineData("eu-central-2")]   // Europe (Zurich) — CH, NOT EEA
    [InlineData("eu-isoe-west-1")] // isolated aws-iso-e partition, not commercial
    public void SesProvider_WithARegionOutsideTheEea_FailsLoud(string region)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", SesSettingsWith(nameof(SesEmailOptions.Region), region)));

        ex.Message.ShouldContain(region);
    }

    /// <summary>
    /// Non-vacuity for the guard above: an EEA region other than the configured default must still
    /// PASS, so the allow-list cannot silently collapse to "only eu-north-1" and read as correct.
    /// </summary>
    [Theory]
    [InlineData("eu-central-1")]
    [InlineData("eu-west-1")]
    [InlineData("eu-south-2")]
    public void SesProvider_WithAnotherEeaRegion_StillRegisters(string region) =>
        ResolveEmailSenderImpl("Development", SesSettingsWith(nameof(SesEmailOptions.Region), region))
            .ShouldBe(typeof(SesEmailSender));

    /// <summary>
    /// The two client-config decisions from senior-cto-advisor's bind, pinned rather than merely
    /// commented (dotnet-architect, 2026-08-08: they had eight grep hits and all eight were prose).
    /// <list type="bullet">
    ///   <item><b><c>MaxErrorRetry = 0</c></b> — SES v2 has no idempotency parameter, so an SDK retry
    ///     of a request whose outcome is unknown is a duplicate delivery, not a recovery. The SDK
    ///     default under <c>RetryMode.Standard</c> is <b>2</b>, so this is a real override and a
    ///     silent revert would restore two possible re-sends of an accepted request.</item>
    ///   <item><b>The region is the configured one</b>, never the SDK's default region chain.</item>
    /// </list>
    /// <para>
    /// This is the ONLY test in the file that RESOLVES the SDK client rather than inspecting the
    /// descriptor. That is deliberate and it is cheap: construction is offline and needs no valid
    /// credentials — no network call happens until a send. Everything else here stays at
    /// descriptor level so the suite keeps its no-client-ever-constructed property.
    /// </para>
    /// </summary>
    [Fact]
    public void SesProvider_ConfiguresTheClientWithZeroRetriesAndTheConfiguredRegion()
    {
        using var provider = BuildServices("Development", FullSesSettings()).BuildServiceProvider();

        var client = provider.GetRequiredService<IAmazonSimpleEmailServiceV2>();

        client.Config.MaxErrorRetry.ShouldBe(0);
        client.Config.RegionEndpoint.SystemName.ShouldBe(Region);
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
