using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Security.BreachCheck;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #616 (R11 pin) — the boot-safety contract of <see cref="BreachCheckOptions"/>: every default is
/// valid, so CI, Testcontainers and dev boot with NO <c>BreachCheck</c> section. The real
/// registration wires <c>ValidateDataAnnotations().ValidateOnStart()</c>, so the moment a default is
/// removed or made invalid (e.g. a default-less <c>[Required]</c> BaseUrl), app start would throw
/// <see cref="OptionsValidationException"/> in EVERY section-less environment. These pins encode
/// that contract with a legible failure, and run the REAL <c>AddBreachedPasswordCheck</c> wiring
/// (not a re-implementation of the AddOptions chain) so they cannot drift from production.
/// </summary>
public class BreachCheckOptionsValidationTests
{
    [Fact]
    public void Options_BindEmptyConfig_AllDefaultsValid_NoBreachCheckSectionNeededToBoot()
    {
        // No throw resolving .Value == the defaults satisfy every data annotation.
        var value = ResolveOptionsFrom([]);

        value.BaseUrl.ShouldBe("https://api.pwnedpasswords.com/");
        // The relative range/{prefix} request URI only resolves under a trailing-slash base.
        value.BaseUrl.ShouldEndWith("/");
        // Opt-out: a missing section must not disable the check.
        value.Enabled.ShouldBeTrue();
        value.TimeoutSeconds.ShouldBe(2);
    }

    [Fact]
    public void Options_InvalidBaseUrl_FailsDataAnnotationsValidation()
    {
        // The [Url] guard is real: a malformed BaseUrl fails validation (→ fail-loud at
        // ValidateOnStart), never a silently-broken BaseAddress at first request.
        Should.Throw<OptionsValidationException>(() =>
            ResolveOptionsFrom(new Dictionary<string, string?> { ["BreachCheck:BaseUrl"] = "not-a-url" }));
    }

    [Fact]
    public void Options_TimeoutOutOfRange_FailsDataAnnotationsValidation()
    {
        // [Range(1, 30)] on the interactive attempt budget — a 0 s / negative timeout is a
        // misconfiguration that must fail app start, not degrade every check to instant fail-open.
        Should.Throw<OptionsValidationException>(() =>
            ResolveOptionsFrom(new Dictionary<string, string?> { ["BreachCheck:TimeoutSeconds"] = "0" }));
    }

    private static BreachCheckOptions ResolveOptionsFrom(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // Exercise the production registration itself — the AddOptions/Bind/ValidateDataAnnotations
        // chain lives inside AddBreachedPasswordCheck; .Value below triggers the same validator that
        // ValidateOnStart would run at host boot.
        services.AddBreachedPasswordCheck(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<BreachCheckOptions>>().Value;
    }
}
