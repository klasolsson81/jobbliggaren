using System.Net;
using Jobbliggaren.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// Unit-test (ej smoke) — verifierar fail-loud-parse av <see cref="ForwardedHeadersConfig"/>.
/// TD-21 / STEG 12: tyst no-op:ad rate-limiting i prod är värre än uppstart-throw.
///
/// Placering i Api.IntegrationTests-projektet är pragmatisk — Jobbliggaren saknar idag
/// ett separat Api.UnitTests-projekt (samma rationale som
/// <c>RateLimitingOptionsTests</c>).
/// </summary>
public class ForwardedHeadersConfigTests
{
    [Fact]
    public void Defaults_PreserveAspNetCoreLoopbackBehavior()
    {
        var sut = new ForwardedHeadersConfig();

        sut.KnownNetworks.ShouldBeEmpty("Tom array = ASP.NET-default (loopback only)");
        sut.KnownProxies.ShouldBeEmpty();
        sut.ForwardLimit.ShouldBe(1, "Default 1 hop räcker i dev (direkt-anrop)");
    }

    [Fact]
    public void SectionName_IsForwardedHeaders()
    {
        ForwardedHeadersConfig.SectionName.ShouldBe("ForwardedHeaders");
    }

    [Fact]
    public void BindsFromConfiguration_ProdLikeOverlay()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/16",
                ["ForwardedHeaders:KnownNetworks:1"] = "172.31.0.0/16",
                ["ForwardedHeaders:ForwardLimit"] = "2",
            })
            .Build();

        var bound = config.GetSection(ForwardedHeadersConfig.SectionName)
            .Get<ForwardedHeadersConfig>();

        bound.ShouldNotBeNull();
        bound.KnownNetworks.ShouldBe(["10.0.0.0/16", "172.31.0.0/16"]);
        bound.ForwardLimit.ShouldBe(2);
    }

    [Fact]
    public void ParseKnownNetworks_AcceptsValidCidrV4()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownNetworks = ["10.0.0.0/16", "192.168.0.0/24"],
        };

        var parsed = sut.ParseKnownNetworks();

        parsed.Count.ShouldBe(2);
        parsed[0].PrefixLength.ShouldBe(16);
        parsed[0].BaseAddress.ShouldBe(IPAddress.Parse("10.0.0.0"));
        parsed[1].PrefixLength.ShouldBe(24);
    }

    [Fact]
    public void ParseKnownNetworks_AcceptsValidCidrV6()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownNetworks = ["2001:db8::/32"],
        };

        var parsed = sut.ParseKnownNetworks();

        parsed.Count.ShouldBe(1);
        parsed[0].PrefixLength.ShouldBe(32);
    }

    [Fact]
    public void ParseKnownNetworks_FailsLoud_OnInvalidCidr()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownNetworks = ["10.0.0.0/16", "not-a-cidr"],
        };

        var ex = Should.Throw<InvalidOperationException>(() => sut.ParseKnownNetworks());

        ex.Message.ShouldContain("KnownNetworks[1]");
        ex.Message.ShouldContain("not-a-cidr");
    }

    [Fact]
    public void ParseKnownNetworks_FailsLoud_OnMissingPrefix()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownNetworks = ["10.0.0.0"],   // saknar /N
        };

        Should.Throw<InvalidOperationException>(() => sut.ParseKnownNetworks());
    }

    [Fact]
    public void ParseKnownProxies_AcceptsValidIp()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownProxies = ["198.51.100.42", "::1"],
        };

        var parsed = sut.ParseKnownProxies();

        parsed.Count.ShouldBe(2);
        parsed[0].ShouldBe(IPAddress.Parse("198.51.100.42"));
        parsed[1].ShouldBe(IPAddress.Parse("::1"));
    }

    [Fact]
    public void ParseKnownProxies_FailsLoud_OnInvalidIp()
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownProxies = ["not-an-ip"],
        };

        var ex = Should.Throw<InvalidOperationException>(() => sut.ParseKnownProxies());

        ex.Message.ShouldContain("KnownProxies[0]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(100)]
    public void ValidateForwardLimit_FailsLoud_OnOutOfRange(int forwardLimit)
    {
        var sut = new ForwardedHeadersConfig { ForwardLimit = forwardLimit };

        Should.Throw<InvalidOperationException>(() => sut.ValidateForwardLimit());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void ValidateForwardLimit_AcceptsValidRange(int forwardLimit)
    {
        var sut = new ForwardedHeadersConfig { ForwardLimit = forwardLimit };

        sut.ValidateForwardLimit().ShouldBe(forwardLimit);
    }

    [Fact]
    public void EmptyArrays_ParseToEmptyResults()
    {
        var sut = new ForwardedHeadersConfig();

        sut.ParseKnownNetworks().ShouldBeEmpty();
        sut.ParseKnownProxies().ShouldBeEmpty();
    }

    // ----- EnsureSafeForEnvironment (Sec-Major-1 production-defense) -----

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Test")]
    [InlineData("test")]
    public void EnsureSafeForEnvironment_AllowsEmptyKnownNetworks_InDevOrTest(string env)
    {
        var sut = new ForwardedHeadersConfig();   // tom KnownNetworks

        Should.NotThrow(() => sut.EnsureSafeForEnvironment(env));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Preprod")]
    [InlineData("Demo")]
    [InlineData("PROD")]
    public void EnsureSafeForEnvironment_FailsLoud_OnEmptyKnownNetworks_OutsideDevTest(string env)
    {
        var sut = new ForwardedHeadersConfig();   // tom KnownNetworks

        var ex = Should.Throw<InvalidOperationException>(() => sut.EnsureSafeForEnvironment(env));

        ex.Message.ShouldContain("KnownNetworks");
        ex.Message.ShouldContain(env);
        ex.Message.ShouldContain("aws-setup.md");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void EnsureSafeForEnvironment_AllowsPopulatedKnownNetworks_InAnyEnv(string env)
    {
        var sut = new ForwardedHeadersConfig
        {
            KnownNetworks = ["10.0.0.0/16"],
        };

        Should.NotThrow(() => sut.EnsureSafeForEnvironment(env));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EnsureSafeForEnvironment_ThrowsArgumentException_OnEmptyEnvironmentName(string? env)
    {
        var sut = new ForwardedHeadersConfig();

        Should.Throw<ArgumentException>(() => sut.EnsureSafeForEnvironment(env!));
    }
}
