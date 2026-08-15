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
/// #183 — DI-gate for the <c>Email:Provider="Scaleway"</c> branch in
/// <see cref="DependencyInjection.AddEmailSender"/>. Successor to the deleted
/// <c>SesEmailProviderGateTests</c>. Pure <see cref="ServiceCollection"/> inspection, with one
/// deliberate exception noted below: no host boot, no Testcontainers, no network, so this suite is
/// offline and credential-free.
///
/// <para>
/// <b>Invariants pinned:</b>
/// <list type="bullet">
///   <item><b>Every missing or blank Scaleway setting fails LOUD at REGISTRATION</b> — region,
///     secret key, project id, each independently. The check is deliberately a raw
///     <see cref="IConfiguration"/> read in the arm rather than a constructor guard in
///     <see cref="ScalewayEmailSender"/>, because <c>AddSingleton&lt;TService, TImpl&gt;</c> is
///     LAZY: a constructor guard would let production boot clean and fail on the FIRST EMAIL
///     instead. A silent no-op that looks like it sends is the failure mode this arm exists to
///     prevent.</item>
///   <item><b>A region outside the allow-list is refused</b>, whether it is a typo or a real
///     Scaleway region the mail service does not run in. Both directions are covered: <c>fr-par</c>
///     registers, everything else throws.</item>
///   <item><b>A complete config resolves <see cref="IEmailSender"/> to
///     <see cref="ScalewayEmailSender"/></b>, in Development and in Production alike — the provider
///     key, not the environment, is what selects this arm (unlike the Console arm).</item>
///   <item><b>The named HTTP client is configured</b>, with the region built into a base address
///     that ENDS IN A SLASH and a timeout well under the <see cref="HttpClient"/> default. Both are
///     properties of the registration rather than of the sender, so this is the only place they can
///     be measured.</item>
///   <item><b><see cref="IEmailSender"/> is registered EXACTLY ONCE, as a Singleton, by TYPE.</b>
///     Asserted as a COUNT, not via <c>.Single()</c> not throwing: a second registration would
///     silently win by last-wins resolution while every <c>.Single()</c>-based helper threw a
///     confusing sequence error instead of naming the duplicate. And by type rather than by factory
///     because <c>ServiceDescriptor.ImplementationType</c> is null for a factory — which is also why
///     the sender is NOT registered as a typed <see cref="HttpClient"/> (those are transient, behind
///     a factory); see <see cref="ScalewayClientRegistration.HttpClientName"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>On the credential fixtures.</b> The arm's only test of a credential is
/// <c>IsNullOrWhiteSpace</c>, so any non-blank string exercises it identically. These are
/// deliberately NOT shaped like a real Scaleway key: a realistic shape would buy nothing
/// behaviourally and would hand the pre-push gitleaks scan a plausible false positive forever. The
/// region IS the real one (<c>fr-par</c>), and that matters more than it looks — the arm resolves
/// the region eagerly at registration and REJECTS anything else, so a made-up region exercises a
/// different path, the one <see cref="ScalewayProvider_WithADisallowedRegion_FailsLoud"/> owns.
/// </para>
/// </summary>
public class ScalewayEmailProviderGateTests
{
    private const string Region = "fr-par";
    private const string SecretKey = "test-scaleway-secret-key";
    private const string ProjectId = "11111111-2222-3333-4444-555555555555";

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

    /// <summary>A complete, valid Scaleway configuration — the baseline each negative case removes ONE key from.</summary>
    private static Dictionary<string, string?> FullScalewaySettings(string provider = "Scaleway") =>
        new()
        {
            [$"{EmailOptions.SectionName}:Provider"] = provider,
            [$"{ScalewayEmailOptions.SectionName}:Region"] = Region,
            [$"{ScalewayEmailOptions.SectionName}:SecretKey"] = SecretKey,
            [$"{ScalewayEmailOptions.SectionName}:ProjectId"] = ProjectId,
        };

    private static Dictionary<string, string?> ScalewaySettingsWith(string key, string? value)
    {
        var settings = FullScalewaySettings();
        if (value is null)
            settings.Remove($"{ScalewayEmailOptions.SectionName}:{key}");
        else
            settings[$"{ScalewayEmailOptions.SectionName}:{key}"] = value;
        return settings;
    }

    /// <summary>
    /// Overrides <c>Email:FromAddress</c> — note the section is <c>Email:</c>, NOT
    /// <c>Email:Scaleway:</c>, so <see cref="ScalewaySettingsWith"/> cannot express it.
    /// </summary>
    private static Dictionary<string, string?> ScalewaySettingsWithFromAddress(string? fromAddress)
    {
        var settings = FullScalewaySettings();
        if (fromAddress is not null)
            settings[$"{EmailOptions.SectionName}:{nameof(EmailOptions.FromAddress)}"] = fromAddress;
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
    public void ScalewayProvider_WithoutARegion_FailsLoud(string? region)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", ScalewaySettingsWith(nameof(ScalewayEmailOptions.Region), region)));

        // Substring only — the guidance sentence is Swedish UI-adjacent prose and may be reworded.
        // What must not drift is WHICH setting the message names, because that is what makes the
        // crash actionable at 02:00.
        ex.Message.ShouldContain("Email:Scaleway:Region");
    }

    /// <summary>
    /// A region name that is well-formed but not allowed must fail at REGISTRATION, and this is a
    /// separate invariant from "blank".
    /// <para>
    /// The rows split into two kinds and both are deliberate. <c>fr-pars</c> and <c>totally-bogus</c>
    /// are the fail-open case: the region is interpolated straight into the endpoint URL, so a typo
    /// builds a perfectly well-formed URL that satisfies <see cref="Uri"/> and every null check the
    /// arm has, and would fail first as a 404 on the first real send, in production, after a deploy.
    /// <c>nl-ams</c> and <c>pl-waw</c> are REAL Scaleway regions inside the EEA — they are refused
    /// because Transactional Email does not run there (measured 2026-08-15), which is a different
    /// reason from the typo and from <c>us-east-2</c>'s (not Scaleway at all, and not EEA). Keeping
    /// all three kinds here is what stops the guard from being "re-fixed" into a prefix check.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("fr-pars")]        // the realistic typo
    [InlineData("totally-bogus")]  // not region-shaped at all
    [InlineData("nl-ams")]         // real Scaleway region, EEA — but no Transactional Email there
    [InlineData("pl-waw")]         // ditto
    [InlineData("us-east-2")]      // neither Scaleway nor EEA
    [InlineData("FR-PAR")]         // casing is NOT normalised — the allow-list is Ordinal
    public void ScalewayProvider_WithADisallowedRegion_FailsLoud(string region)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", ScalewaySettingsWith(nameof(ScalewayEmailOptions.Region), region)));

        ex.Message.ShouldContain(region);
    }

    /// <summary>
    /// Non-vacuity for the guard above: the one allowed region must actually register, so the
    /// allow-list cannot collapse to "refuse everything" and read as strict.
    /// </summary>
    [Fact]
    public void ScalewayProvider_WithTheAllowedRegion_Registers() =>
        ResolveEmailSenderImpl("Development", FullScalewaySettings())
            .ShouldBe(typeof(ScalewayEmailSender));

    // --- Fail loud: the two secrets, each independently ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ScalewayProvider_WithoutASecretKey_FailsLoud(string? secretKey)
    {
        // The Region IS supplied here, so this can only fail on the credential guard — otherwise the
        // test would pass on the region guard and prove nothing about credentials.
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices(
                "Development",
                ScalewaySettingsWith(nameof(ScalewayEmailOptions.SecretKey), secretKey)));

        ex.Message.ShouldContain("Email:Scaleway:SecretKey");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ScalewayProvider_WithoutAProjectId_FailsLoud(string? projectId)
    {
        // TWO secrets, not two halves of one — and the message must name WHICH is missing. The
        // secret key IS supplied here, so a guard that only checked the key would let this through.
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices(
                "Development",
                ScalewaySettingsWith(nameof(ScalewayEmailOptions.ProjectId), projectId)));

        ex.Message.ShouldContain("Email:Scaleway:ProjectId");
    }

    [Fact]
    public void ScalewayProvider_WithNoScalewaySectionAtAll_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices(
            "Production",
            new Dictionary<string, string?>
            {
                [$"{EmailOptions.SectionName}:Provider"] = "Scaleway",
            }));

    // --- Fail loud: from address ---

    /// <summary>
    /// The <c>FromAddress</c> gate. Not cosmetic: <c>_dmarc.jobbliggaren.se</c> publishes
    /// <c>p=reject</c> with no <c>rua=</c> (measured 2026-08-08, ADR 0124), so a sender address
    /// outside the DKIM-verified identity is not an error — it is total, silent, unreported delivery
    /// loss. The domain is Verified at Scaleway; the DMARC posture is unchanged by the provider swap.
    /// <para>
    /// The empty and whitespace rows matter beyond "blank is rejected": the DI arm reads the key
    /// with <c>?? new EmailOptions().FromAddress</c>, so a null coalesces to the valid default and
    /// must NOT throw, while an explicitly empty value must. Those are different code paths.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]                  // explicitly empty — the fallback must not rescue it
    [InlineData("   ")]               // whitespace
    [InlineData("no-reply")]          // no @ — a local part someone forgot to qualify
    [InlineData("jobbliggaren.se")]   // a domain pasted where an address belongs
    public void ScalewayProvider_WithAnUnusableFromAddress_FailsLoud(string fromAddress)
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            BuildServices("Development", ScalewaySettingsWithFromAddress(fromAddress)));

        ex.Message.ShouldContain("Email:FromAddress");
    }

    /// <summary>
    /// Non-vacuity for the gate above, and it pins the coalesce specifically: with the key ABSENT
    /// the arm falls back to <see cref="EmailOptions"/>'s default, which is a valid address, so
    /// registration must succeed. Without this row the theory above would still pass if the gate
    /// had been written to reject every unset value.
    /// </summary>
    [Fact]
    public void ScalewayProvider_WithNoFromAddressConfigured_FallsBackToTheDefaultAndRegisters() =>
        ResolveEmailSenderImpl("Development", ScalewaySettingsWithFromAddress(null))
            .ShouldBe(typeof(ScalewayEmailSender));

    // --- The named client the sender resolves ---

    /// <summary>
    /// The registration's own two decisions, pinned rather than merely commented.
    /// <list type="bullet">
    ///   <item><b>The base address carries the region AND ends in a slash.</b> Relative-URI
    ///     resolution replaces the last segment of a base that lacks one, so without the trailing
    ///     slash the sender's <c>"emails"</c> would resolve against <c>…/regions/</c> and drop the
    ///     region entirely — producing a wrong URL that still looks region-aware in the source. The
    ///     resolved send URL is asserted, not just the base, because that is the thing that must be
    ///     right.</item>
    ///   <item><b>The timeout is bounded and is not the <see cref="HttpClient"/> default</b> of 100
    ///     seconds. There is no retry anywhere on this path, so this is the only bound that exists,
    ///     and <c>DigestDispatchJob</c> would otherwise hold the default PER RECIPIENT against an
    ///     unreachable provider. Asserted as a band rather than an exact literal: the property is
    ///     "a real bound, far under the default", and pinning the number would make this a
    ///     change-detector for a value nothing else depends on.</item>
    /// </list>
    /// <para>
    /// This is the ONLY test in the file that BUILDS the provider rather than inspecting descriptors.
    /// That is deliberate and cheap: constructing an <see cref="HttpClient"/> is offline and needs no
    /// credentials — nothing reaches the network until a send.
    /// </para>
    /// </summary>
    [Fact]
    public void ScalewayProvider_ConfiguresTheNamedClientWithARegionalBaseAddressAndABoundedTimeout()
    {
        using var provider = BuildServices("Development", FullScalewaySettings()).BuildServiceProvider();

        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ScalewayClientRegistration.HttpClientName);

        var baseAddress = client.BaseAddress.ShouldNotBeNull();
        baseAddress.AbsoluteUri.ShouldBe(
            $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/");
        new Uri(baseAddress, "emails").AbsoluteUri.ShouldBe(
            $"https://api.scaleway.com/transactional-email/v1alpha1/regions/{Region}/emails");

        client.Timeout.ShouldBeLessThan(
            TimeSpan.FromSeconds(100), "that is the HttpClient default — no explicit bound was set");
        client.Timeout.ShouldBeGreaterThan(TimeSpan.FromSeconds(5));
    }

    // --- Happy path: the arm registers the Scaleway sender ---

    // Unlike the Console arm, this one is environment-INDEPENDENT: the provider key alone selects
    // it. Pinned across all four environment names so a future "Scaleway only outside dev" gate
    // cannot be added without this failing.
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void ScalewayProvider_WithFullConfiguration_RegistersScalewayEmailSender(string env) =>
        ResolveEmailSenderImpl(env, FullScalewaySettings()).ShouldBe(typeof(ScalewayEmailSender));

    [Theory]
    [InlineData("scaleway")]
    [InlineData("SCALEWAY")]
    [InlineData("ScAlEwAy")]
    public void ScalewayProvider_IsCaseInsensitive(string provider) =>
        ResolveEmailSenderImpl("Development", FullScalewaySettings(provider))
            .ShouldBe(typeof(ScalewayEmailSender));

    [Fact]
    public void ScalewayProvider_WithFullConfiguration_RegistersTheHttpClientFactory()
    {
        // ScalewayEmailSender depends on IHttpClientFactory — proves the client registration ran,
        // not just the sender. AddHttpClient registers it behind a factory, which is why this
        // asserts on ServiceType rather than on an implementation type.
        var services = BuildServices("Production", FullScalewaySettings());

        services.ShouldContain(d => d.ServiceType == typeof(IHttpClientFactory));
    }

    // --- Exactly one IEmailSender, Singleton, by type ---

    [Fact]
    public void ScalewayProvider_WithFullConfiguration_RegistersExactlyOneEmailSender()
    {
        var services = BuildServices("Production", FullScalewaySettings());

        // COUNT, not .Single(). A duplicate registration resolves last-wins and would silently
        // shadow the Scaleway sender; .Single() would throw a sequence error that names nothing.
        services.Count(d => d.ServiceType == typeof(IEmailSender)).ShouldBe(1);
    }

    [Fact]
    public void ScalewayProvider_WithFullConfiguration_RegistersTheEmailSenderAsASingletonByType()
    {
        var services = BuildServices("Production", FullScalewaySettings());

        var descriptor = services.Single(d => d.ServiceType == typeof(IEmailSender));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        // ImplementationType (not a factory lambda, and not a typed HttpClient — those are transient
        // behind a factory) is load-bearing for every impl-type assertion in this file and in
        // AddEmailSenderGateTests. A singleton that resolves its HttpClient per send via
        // IHttpClientFactory is what keeps handler rotation alive without a typed client.
        descriptor.ImplementationType.ShouldBe(typeof(ScalewayEmailSender));
    }

    // --- Regression: the Console/Null gate is untouched by the Scaleway arm ---

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
    public void ConsoleProvider_WithScalewayCredentialsPresent_StillRegistersConsoleEmailSender()
    {
        // The Scaleway keys being PRESENT must not select the Scaleway arm — only Email:Provider
        // does. This is the state a dev box lands in the moment someone pastes prod credentials into
        // appsettings.Local.json to test something and leaves Provider on its default.
        var settings = FullScalewaySettings(provider: "Console");

        ResolveEmailSenderImpl("Development", settings).ShouldBe(typeof(ConsoleEmailSender));
        // And the HTTP client is not registered either: AddEmailSender wires it ONLY inside the
        // Scaleway arm, so its absence here proves the arm was not entered.
        BuildServices("Development", settings)
            .ShouldNotContain(d => d.ServiceType == typeof(IHttpClientFactory));
    }
}
