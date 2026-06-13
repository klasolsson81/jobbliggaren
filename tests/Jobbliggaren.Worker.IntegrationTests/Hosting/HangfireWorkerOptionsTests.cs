using Jobbliggaren.Worker.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// Unit-test (ej smoke) — verifierar defaults + config-binding för
/// <see cref="HangfireWorkerOptions"/>. Per TD-17 är det kritiskt att
/// dev-default = säker-att-köra-lokalt och non-dev-overlay tvingas
/// (fail-loud i Worker/Program.cs om PrepareSchemaIfNecessary=true utanför
/// Development/Test).
///
/// Placering i <c>Worker.IntegrationTests</c>-projektet är pragmatisk —
/// Jobbliggaren saknar idag ett separat <c>Worker.UnitTests</c>-projekt. När
/// det skapas (Fas 2 vid Worker-jobb-yta-tillväxt) flyttas dessa tester dit.
/// </summary>
public class HangfireWorkerOptionsTests
{
    [Fact]
    public void Defaults_AreDevFriendly()
    {
        var sut = new HangfireWorkerOptions();

        sut.PrepareSchemaIfNecessary.ShouldBeTrue("Dev-default — Worker skapar schema vid uppstart");
        sut.ShutdownTimeoutSeconds.ShouldBe(25, "Strax under Fargate default stopTimeout 30s");
    }

    [Fact]
    public void SectionName_IsHangfire()
    {
        HangfireWorkerOptions.SectionName.ShouldBe("Hangfire");
    }

    [Fact]
    public void BindsFromConfiguration_OverlayValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:PrepareSchemaIfNecessary"] = "false",
                ["Hangfire:ShutdownTimeoutSeconds"] = "60"
            })
            .Build();

        var bound = config.GetSection(HangfireWorkerOptions.SectionName)
            .Get<HangfireWorkerOptions>();

        bound.ShouldNotBeNull();
        bound.PrepareSchemaIfNecessary.ShouldBeFalse();
        bound.ShutdownTimeoutSeconds.ShouldBe(60);
    }

    [Fact]
    public void BindsFromConfiguration_PartialOverlay_KeepsDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:PrepareSchemaIfNecessary"] = "false"
            })
            .Build();

        var bound = config.GetSection(HangfireWorkerOptions.SectionName)
            .Get<HangfireWorkerOptions>();

        bound.ShouldNotBeNull();
        bound.PrepareSchemaIfNecessary.ShouldBeFalse();
        bound.ShutdownTimeoutSeconds.ShouldBe(25, "Default bevaras vid partial overlay");
    }

    [Fact]
    public void MissingSection_ReturnsNull_HandledByCallerWithDefaults()
    {
        // Worker/Program.cs faller tillbaka på `?? new HangfireWorkerOptions()`
        // om sektionen saknas helt. Verifierar att .Get<> returnerar null så
        // den fallback-vägen tränas.
        var config = new ConfigurationBuilder().Build();

        var bound = config.GetSection(HangfireWorkerOptions.SectionName)
            .Get<HangfireWorkerOptions>();

        bound.ShouldBeNull();
    }
}
