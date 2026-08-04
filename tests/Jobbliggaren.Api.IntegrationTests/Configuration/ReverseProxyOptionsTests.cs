using Jobbliggaren.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// Unit-test (ej smoke) — verifierar config-bindning för <see cref="ReverseProxyOptions"/>.
/// Placering i Api.IntegrationTests-projektet följer samma rationale som
/// <c>HstsOptionsTests</c> + <c>ForwardedHeadersConfigTests</c> + <c>RateLimitingOptionsTests</c>.
///
/// <para>
/// <b>Varför sektionsnyckeln behöver en egen pinne (#196).</b> <c>UseHttpsRedirectionGateTests</c>
/// fångar en bruten sektionsnyckel — men bara <b>indirekt och odiagnostiskt</b>. En nyckel som
/// slutar binda ger <c>false</c>, och <c>false</c> är exakt vad Disabled- och
/// Development-factory:erna asserterar, så bara Enabled-factory:ns två fakta går röda — 2 av 6,
/// och ingen av dem nämner sektionsnyckeln. Felet läser som "Enabled-factory ger 200 i stället
/// för 307", vilket lika gärna kan vara en pipeline-ordningsbugg, och felsökningen börjar på fel
/// ställe. Denna klass gör nyckeln till ett eget, namngivet faktum.
/// </para>
///
/// <para>
/// <b>Vad den INTE täcker.</b> Testerna anropar <c>GetSection(...).Get&lt;T&gt;()</c> direkt och rör
/// aldrig <c>Program.cs</c>, så de pinnar framework-nivåns sektionsmatchning — inte appens
/// komposition. En fallback-bind tillagd i <c>Program.cs</c>
/// (<c>?? GetSection("Alb").Get&lt;...&gt;()</c>) skulle passera grönt här. Och HSTS-valideringsgaten
/// vid service-registrering pinnas fortfarande av ingenting; denna klass ändrar inte det.
/// Ett fjärde <c>WebApplicationFactory</c> hade kunnat pinna kompositionen på riktigt, men
/// avstods medvetet: sviten ligger en <c>WebApplicationFactory</c> under EF:s process-globala
/// <c>ManyServiceProvidersCreatedWarning</c>-tak, och nästa host fäller den collection-fixture
/// som råkar initieras därnäst — ett deterministiskt fel som läser som flake (#1190).
/// </para>
/// </summary>
public class ReverseProxyOptionsTests
{
    [Fact]
    public void SectionName_IsReverseProxy()
    {
        ReverseProxyOptions.SectionName.ShouldBe("ReverseProxy");
    }

    [Fact]
    public void Default_HttpsDisabled()
    {
        // Correct under Option B, not a placeholder: Next reaches the API over plain internal
        // HTTP, so a true here would 307 every internal call. See ReverseProxyOptions.
        new ReverseProxyOptions().HttpsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:HttpsEnabled"] = "true",
            })
            .Build();

        var bound = config.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>();

        bound.ShouldNotBeNull();
        bound.HttpsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void LegacyAlbSectionKey_NoLongerBinds()
    {
        // Deliberate: the retired "Alb" key has NO transitional fallback (#196). Measured
        // before removal — no "Alb" section existed in any appsettings file, so the option
        // bound false everywhere, and the only injector was the ECS task-definition ADR 0066
        // destroyed. This fact exists so the absence is a decision on record rather than an
        // oversight a future reader has to re-derive.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Alb:HttpsEnabled"] = "true",
            })
            .Build();

        var bound = config.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>();

        // The section is absent entirely, so Get<T>() yields null — collapse both shapes
        // (null, or bound-but-false) into the one fact that matters: it does not bind true.
        (bound?.HttpsEnabled ?? false).ShouldBeFalse(
            "the Alb section key was retired with #196 and has no fallback bind by design");
    }
}
