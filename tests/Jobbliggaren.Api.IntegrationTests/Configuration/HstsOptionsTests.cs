using Jobbliggaren.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// Unit-test (ej smoke) — verifierar config-bindning + production-defense för
/// <see cref="HstsOptions"/>. STEG 13c / security-auditor Sec-Major-2 +
/// dotnet-architect-fynd 5 (EnsureSafeForEnvironment-paritet med
/// ForwardedHeadersConfig).
///
/// Pipeline-gating-tester (UseHsts() registreras vs hoppas över per Environment +
/// HttpsEnabled, plus header-närvaro i HTTPS-svar) ligger separat (TD-32 /
/// UseHttpsRedirectionGateTests-pattern via WebApplicationFactory).
///
/// Placering i Api.IntegrationTests-projektet följer samma rationale som
/// <c>ForwardedHeadersConfigTests</c> + <c>RateLimitingOptionsTests</c>.
/// </summary>
public class HstsOptionsTests
{
    [Fact]
    public void Defaults_MatchHstsSpecRecommendation()
    {
        var sut = new HstsOptions();

        sut.MaxAgeDays.ShouldBe(365, "HSTS-spec + hstspreload.org-krav är >= 1 år");
        sut.IncludeSubDomains.ShouldBeTrue("Default skydd för alla subdomäner");
        sut.Preload.ShouldBeFalse("Preload kräver hstspreload.org-submission post-prod-launch");
    }

    [Fact]
    public void SectionName_IsHsts()
    {
        HstsOptions.SectionName.ShouldBe("Hsts");
    }

    [Fact]
    public void BindsFromConfiguration_ProductionOverlay()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hsts:MaxAgeDays"] = "730",
                ["Hsts:IncludeSubDomains"] = "false",
                ["Hsts:Preload"] = "true",
            })
            .Build();

        var bound = config.GetSection(HstsOptions.SectionName).Get<HstsOptions>();

        bound.ShouldNotBeNull();
        bound.MaxAgeDays.ShouldBe(730);
        bound.IncludeSubDomains.ShouldBeFalse();
        bound.Preload.ShouldBeTrue();
    }

    // ----- EnsureSafeForEnvironment (dotnet-architect Viktigt-fynd 5) -----

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Test")]
    [InlineData("test")]
    public void EnsureSafeForEnvironment_AllowsAnyConfig_InDevOrTest(string env)
    {
        // I dev/test ska alla konfigurationer accepteras — även sådana som vore
        // farliga i prod (MaxAgeDays=0, Preload utan IncludeSubDomains, etc).
        var risky = new HstsOptions
        {
            MaxAgeDays = 0,
            IncludeSubDomains = false,
            Preload = true,
        };

        Should.NotThrow(() => risky.EnsureSafeForEnvironment(env));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("PROD")]
    [InlineData("Demo")]
    public void EnsureSafeForEnvironment_FailsLoud_OnLowMaxAgeDays_OutsideDevTest(string env)
    {
        var sut = new HstsOptions { MaxAgeDays = 30, IncludeSubDomains = true };

        var ex = Should.Throw<InvalidOperationException>(() => sut.EnsureSafeForEnvironment(env));

        ex.Message.ShouldContain("MaxAgeDays");
        ex.Message.ShouldContain("365");
        ex.Message.ShouldContain(env);
    }

    [Fact]
    public void EnsureSafeForEnvironment_AcceptsSpecCompliantDefaults_InProduction()
    {
        var sut = new HstsOptions();   // defaults: 365 + includeSubDomains + !preload

        Should.NotThrow(() => sut.EnsureSafeForEnvironment("Production"));
    }

    [Fact]
    public void EnsureSafeForEnvironment_FailsLoud_OnPreloadWithoutIncludeSubDomains()
    {
        var sut = new HstsOptions
        {
            MaxAgeDays = 365,
            IncludeSubDomains = false,
            Preload = true,
        };

        var ex = Should.Throw<InvalidOperationException>(() => sut.EnsureSafeForEnvironment("Production"));

        ex.Message.ShouldContain("Preload");
        ex.Message.ShouldContain("IncludeSubDomains");
    }

    [Fact]
    public void EnsureSafeForEnvironment_AcceptsValidPreloadConfig()
    {
        var sut = new HstsOptions
        {
            MaxAgeDays = 365,
            IncludeSubDomains = true,
            Preload = true,
        };

        Should.NotThrow(() => sut.EnsureSafeForEnvironment("Production"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EnsureSafeForEnvironment_ThrowsArgumentException_OnEmptyEnvironmentName(string? env)
    {
        var sut = new HstsOptions();

        Should.Throw<ArgumentException>(() => sut.EnsureSafeForEnvironment(env!));
    }
}
