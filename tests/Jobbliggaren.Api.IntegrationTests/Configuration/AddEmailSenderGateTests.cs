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
/// ADR 0080 Vag 4 PR-4b (provider swapped in ADR 0124 / #1237) — DI-gate for
/// <see cref="DependencyInjection.AddEmailSender"/>, the email provider-switch that BOTH the Api
/// (via AddInfrastructure) AND the HTTP-free Worker (ADR 0023) call to register
/// <see cref="IEmailSender"/> for the Vag 4 match-notification jobs. Pins the gate both rely on:
/// Dev/Test → Console, otherwise → Null, and an unrecognised provider fails LOUD (never a silent
/// no-op that looks like it sends). Pure registration inspection — no host boot / Testcontainers.
/// (The Ses arm's own configuration facts live in <see cref="SesEmailProviderGateTests"/>; what this
/// file adds about Ses is only what the DEFAULT path needs in order to mean anything.)
/// </summary>
public class AddEmailSenderGateTests
{
    // ADR 0124: eu-north-1 is the real region; the credentials are deliberately not AWS-key-shaped
    // (the arm only tests IsNullOrWhiteSpace, and an AKIA-shaped literal would feed the pre-push
    // gitleaks scan a permanent false positive).
    private const string SesRegion = "eu-north-1";
    private const string SesAccessKeyId = "test-access-key-id";
    private const string SesSecretAccessKey = "test-secret-access-key";

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

    private static Dictionary<string, string?> FullSesSettings() =>
        new()
        {
            [$"{EmailOptions.SectionName}:Provider"] = "Ses",
            [$"{SesEmailOptions.SectionName}:Region"] = SesRegion,
            [$"{SesEmailOptions.SectionName}:AccessKeyId"] = SesAccessKeyId,
            [$"{SesEmailOptions.SectionName}:SecretAccessKey"] = SesSecretAccessKey,
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
    // "Provider unset in Production → NullEmailSender" is VACUOUSLY TRUE if the Ses arm is dead
    // code: a switch with exactly one reachable branch always lands on that branch, and the four
    // facts above would go on passing unchanged while no configuration on earth could produce a
    // real sender. The control and the crossing arm therefore live together, run in the SAME
    // environment (Production), and differ in EXACTLY ONE input — the Email:Provider key. Only the
    // pair proves that the default is a CHOICE rather than the only option.
    //
    // Do not delete the positive arm as "already covered by SesEmailProviderGateTests". That file
    // proves the Ses branch works; only this pair proves the DEFAULT still means something in a
    // world where it does. Both arms live in ONE test so a later tidy-up cannot separate them and
    // leave the control standing alone, which is the exact state this comment exists to prevent.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AddEmailSender_SameEnvironmentDifferentProvider_CrossesTheDefaultIntoTheSesArm()
    {
        // Control: no Email:Provider key at all, Production.
        ResolveEmailSenderImpl("Production", provider: null).ShouldBe(typeof(NullEmailSender));

        // Crossing arm: the SAME environment, exactly one input changed.
        ResolveEmailSenderImpl("Production", FullSesSettings()).ShouldBe(typeof(SesEmailSender));
    }

    // ---------------------------------------------------------------------------------------
    // Near-miss rejection: the provider match must stay EXACT-and-case-insensitive.
    //
    // A refactor to StartsWith/Contains/EndsWith would keep every positive fact in this repo green
    // — "Ses" and "ses" would still resolve — while quietly admitting values nobody vetted. These
    // are the values that separate the three: "AmazonSes" passes EndsWith/Contains, "SesV2" passes
    // StartsWith/Contains, and "Ses " (trailing space, which is what a config file or an env var
    // with a stray space actually produces) passes a Trim()-ing comparison. All three must throw.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("AmazonSes")]  // passes EndsWith / Contains
    [InlineData("SesV2")]      // passes StartsWith / Contains
    [InlineData("Ses ")]       // passes a Trim()-ing comparison; what a stray config space produces
    [InlineData(" Ses")]       // ditto, leading
    [InlineData("Resend")]     // the retired provider (ADR 0124) is now just another unknown value
    public void AddEmailSender_NearMissProviderValue_FailsLoud(string provider)
    {
        // Supplied WITH full SES credentials, so a near miss cannot be waved through on the grounds
        // that the SES section was incomplete — the only thing wrong here is the provider NAME.
        var values = FullSesSettings();
        values[$"{EmailOptions.SectionName}:Provider"] = provider;

        Should.Throw<InvalidOperationException>(() => BuildServices("Development", values));
    }

    [Fact]
    public void AddEmailSender_UnknownProvider_NamesSesAmongTheSupportedValues()
    {
        // Substring only, never the whole Swedish sentence. "Smtp" is chosen deliberately as the
        // rejected value: a value containing "Ses" would satisfy this assertion via the message's
        // echo of the offending input rather than via its guidance clause, and the test would pass
        // even if the guidance still advertised the retired provider.
        var ex = Should.Throw<InvalidOperationException>(() => BuildServices("Development", "Smtp"));

        ex.Message.ShouldContain("'Ses'");
        ex.Message.ShouldContain("'Console'");
    }
}
