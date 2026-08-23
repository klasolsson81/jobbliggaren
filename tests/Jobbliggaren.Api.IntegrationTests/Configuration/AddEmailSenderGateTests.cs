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
/// ADR 0080 Vag 4 PR-4b (provider swapped in ADR 0124 / #1237, then again in #183) — DI-gate for
/// <see cref="DependencyInjection.AddEmailSender"/>, the email provider-switch that BOTH the Api
/// (via AddInfrastructure) AND the HTTP-free Worker (ADR 0023) call to register
/// <see cref="IEmailSender"/> for the Vag 4 match-notification jobs. Pins the gate both rely on:
/// Dev/Test → Console, otherwise → Null, and an unrecognised provider fails LOUD (never a silent
/// no-op that looks like it sends). Pure registration inspection — no host boot / Testcontainers.
/// (The Scaleway arm's own configuration facts live in <see cref="ScalewayEmailProviderGateTests"/>;
/// what this file adds about Scaleway is only what the DEFAULT path needs in order to mean anything.)
/// </summary>
public class AddEmailSenderGateTests
{
    // #183: fr-par is the real region - and the only allowed one, so a made-up value would exercise
    // the region guard instead of the path this file is about. The credentials are deliberately not
    // shaped like real Scaleway values (the arm only tests IsNullOrWhiteSpace, and a realistic
    // literal would feed the pre-push gitleaks scan a permanent false positive).
    private const string ScalewayRegion = "fr-par";
    private const string ScalewaySecretKey = "test-scaleway-secret-key";
    private const string ScalewayProjectId = "11111111-2222-3333-4444-555555555555";

    private static ServiceCollection BuildServices(string environmentName, string? provider = null)
    {
        var values = new Dictionary<string, string?>();
        if (provider is not null)
            values[$"{EmailOptions.SectionName}:Provider"] = provider;
        return BuildServices(environmentName, values);
    }

    private static ServiceCollection BuildServices(
        string environmentName, IReadOnlyDictionary<string, string?> values)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddEmailSender(config, env);
        return services;
    }

    private static Type? ResolveEmailSenderImpl(string environmentName, string? provider = null) =>
        BuildServices(environmentName, provider)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    private static Type? ResolveEmailSenderImpl(
        string environmentName, IReadOnlyDictionary<string, string?> values) =>
        BuildServices(environmentName, values)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    private static Dictionary<string, string?> FullScalewaySettings() =>
        new()
        {
            [$"{EmailOptions.SectionName}:Provider"] = "Scaleway",
            [$"{ScalewayEmailOptions.SectionName}:Region"] = ScalewayRegion,
            [$"{ScalewayEmailOptions.SectionName}:SecretKey"] = ScalewaySecretKey,
            [$"{ScalewayEmailOptions.SectionName}:ProjectId"] = ScalewayProjectId,
        };

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
    public void AddEmailSender_UnknownProvider_FailsLoud() =>
        Should.Throw<InvalidOperationException>(() => BuildServices("Development", "Smtp"));

    // ---------------------------------------------------------------------------------------
    // The crossing counterfactual for the default-path facts above (ADR 0124 / #1237).
    //
    // "Provider unset in Production → NullEmailSender" is VACUOUSLY TRUE if the Scaleway arm is dead
    // code: a switch with exactly one reachable branch always lands on that branch, and the four
    // facts above would go on passing unchanged while no configuration on earth could produce a
    // real sender. The control and the crossing arm therefore live together, run in the SAME
    // environment (Production), and differ in EXACTLY ONE input — the Email:Provider key. Only the
    // pair proves that the default is a CHOICE rather than the only option.
    //
    // Do not delete the positive arm as "already covered by ScalewayEmailProviderGateTests". That
    // file proves the Scaleway branch works; only this pair proves the DEFAULT still means something in a
    // world where it does. Both arms live in ONE test so a later tidy-up cannot separate them and
    // leave the control standing alone, which is the exact state this comment exists to prevent.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AddEmailSender_SameEnvironmentDifferentProvider_CrossesTheDefaultIntoTheScalewayArm()
    {
        // Control: no Email:Provider key at all, Production.
        ResolveEmailSenderImpl("Production", provider: null).ShouldBe(typeof(NullEmailSender));

        // Crossing arm: the SAME environment, exactly one input changed.
        ResolveEmailSenderImpl("Production", FullScalewaySettings())
            .ShouldBe(typeof(ScalewayEmailSender));
    }

    // ---------------------------------------------------------------------------------------
    // Near-miss rejection: the provider match must stay EXACT-and-case-insensitive.
    //
    // A refactor to StartsWith/Contains/EndsWith would keep every positive fact in this repo green
    // - "Scaleway" and "scaleway" would still resolve - while quietly admitting values nobody
    // vetted. These are the values that separate the three: "MyScaleway" passes EndsWith/Contains,
    // "ScalewayTem" passes StartsWith/Contains, and "Scaleway " (trailing space, which is what a
    // config file or an env var with a stray space actually produces) passes a Trim()-ing
    // comparison. All three must throw.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("MyScaleway")]   // passes EndsWith / Contains
    [InlineData("ScalewayTem")]  // passes StartsWith / Contains
    [InlineData("Scaleway ")]    // passes a Trim()-ing comparison; what a stray config space produces
    [InlineData(" Scaleway")]    // ditto, leading
    [InlineData("Ses")]          // the retired provider (#183) is now just another unknown value
    [InlineData("Resend")]       // and so is the one before it (ADR 0124)
    public void AddEmailSender_NearMissProviderValue_FailsLoud(string provider)
    {
        // Supplied WITH full Scaleway credentials, so a near miss cannot be waved through on the
        // grounds that the section was incomplete - the only thing wrong here is the provider NAME.
        var values = FullScalewaySettings();
        values[$"{EmailOptions.SectionName}:Provider"] = provider;

        Should.Throw<InvalidOperationException>(() => BuildServices("Development", values));
    }

    [Fact]
    public void AddEmailSender_UnknownProvider_NamesScalewayAmongTheSupportedValues()
    {
        // Substring only, never the whole Swedish sentence. "Smtp" is chosen deliberately as the
        // rejected value: a value containing "Scaleway" would satisfy this assertion via the
        // message's echo of the offending input rather than via its guidance clause, and the test
        // would pass even if the guidance still advertised a retired provider.
        var ex = Should.Throw<InvalidOperationException>(() => BuildServices("Development", "Smtp"));

        ex.Message.ShouldContain("'Scaleway'");
        ex.Message.ShouldContain("'Console'");
        // And it must no longer advertise either retired provider (#183, ADR 0124).
        ex.Message.ShouldNotContain("'Ses'");
        ex.Message.ShouldNotContain("'Resend'");
    }

    // ---------------------------------------------------------------------------------------
    // #1087 — CanDeliver, at the layer that decides WHICH sender exists.
    //
    // This is the RULE half and it is vacuous on its own: it would stay green forever against a
    // handler that never asks. The call-site pins are ChangeEmailCommandHandlerTests' refusal pair
    // and ChangeEmailTests' 503 crossing; this file only establishes that the registration the
    // environment produces answers the way those pins assume.
    //
    // Worth having anyway, and for a reason the handler pins cannot cover: the mapping from
    // ENVIRONMENT to capability is what this switch decides, and a future edit that registered
    // ConsoleEmailSender outside Dev/Test — or Null inside it — would leave every handler test green
    // while changing which environments can complete an email change.
    // ---------------------------------------------------------------------------------------

    private static bool ResolveCanDeliver(
        string environmentName, IReadOnlyDictionary<string, string?> values)
    {
        var services = BuildServices(environmentName, values);
        using var provider = services.AddLogging().BuildServiceProvider();
        return provider.GetRequiredService<IEmailSender>().CanDeliver;
    }

    private static bool ResolveCanDeliver(string environmentName, string? provider = null) =>
        ResolveCanDeliver(
            environmentName,
            provider is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [$"{EmailOptions.SectionName}:Provider"] = provider });

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AddEmailSender_TheLiveDefaultOutsideDevelopment_CannotDeliver(string env) =>
        // Provider unset — the documented, committed default. This is the state production is in
        // today, and it is the whole reason #1087 exists.
        ResolveCanDeliver(env, provider: null).ShouldBeFalse();

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void AddEmailSender_InDevelopmentOrTest_CanDeliver(string env) =>
        // ConsoleEmailSender writes the whole body — activation and confirmation links included — to
        // ILogger for a recipient at an RFC 2606/6761-reserved domain (#1208), so a developer can
        // complete a token→email→confirm flow from the log. Answering false here would refuse the
        // very flows dev exists to exercise.
        ResolveCanDeliver(env, provider: null).ShouldBeTrue();

    [Fact]
    public void AddEmailSender_CapabilityCrossesOnTheProviderKeyAlone()
    {
        // Same environment, exactly one input changed — the pair, not the halves. Either assertion
        // alone is compatible with a capability that is hard-coded: all-false would pass the control,
        // all-true would pass the crossing arm. Only the pair shows the value tracks the switch.
        ResolveCanDeliver("Production", provider: null).ShouldBeFalse();
        ResolveCanDeliver("Production", FullScalewaySettings()).ShouldBeTrue();
    }
}
