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
/// pinnar pipeline-gaten i båda polariteter, men den kan strukturellt INTE fånga en bruten
/// sektionsnyckel: en nyckel som slutar binda ger <c>false</c>, och <c>false</c> är exakt vad
/// Disabled- och Development-factory:erna asserterar. Bara Enabled-factory:ns två fakta går röda
/// — 2 av 6. Och den tredje konsumenten, HSTS-valideringsgaten vid service-registrering
/// (<c>if (reverseProxyConfig.HttpsEnabled) hstsConfig.EnsureSafeForEnvironment(...)</c>), pinnas
/// av ingenting alls; CLAUDE.md §11 noterar det uttryckligen. Ett namnbyte som bröt just den
/// vägen hade varit grönt överallt. Denna klass gör nyckeln till ett eget faktum.
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
