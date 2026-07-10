using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Security.BreachCheck;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #616 — pins the opt-OUT kill-switch contract of <c>AddBreachedPasswordCheck</c> (CTO-bind
/// FORK 2 / BreachCheckOptions doc): a MISSING section means ENABLED (the check must never be
/// silently disabled — that would be an invisible security regression), and only an explicit
/// <c>BreachCheck:Enabled=false</c> swaps in <see cref="DisabledBreachedPasswordChecker"/>. Both
/// modes register the SAME <see cref="IBreachedPasswordChecker"/> port so the Identity validator
/// chain stays identical. A plain <see cref="ServiceCollection"/> — no Testcontainers/host — is
/// enough (JobTechStreamResilienceTests / HibpBreachCheckResilienceTests parity: the DI graph is
/// registered independently of the Identity/Postgres stack).
/// </summary>
public class BreachCheckRegistrationTests
{
    [Fact]
    public void AddBreachedPasswordCheck_WhenEnabledFalse_RegistersDisabledChecker()
    {
        using var provider = BuildProvider(enabled: false);

        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        checker.ShouldBeOfType<DisabledBreachedPasswordChecker>();
    }

    [Fact]
    public async Task AddBreachedPasswordCheck_WhenEnabledFalse_CheckerAlwaysReportsNotBreached()
    {
        // The kill switch is a NO-OP, not a block: even a would-be-breached password passes so
        // offline-dev / HIBP-emergency mode never wedges registration or password change.
        using var provider = BuildProvider(enabled: false);
        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        var verdict = await checker.CheckAsync("password", TestContext.Current.CancellationToken);

        verdict.ShouldBe(BreachCheckVerdict.NotBreached);
    }

    [Fact]
    public void AddBreachedPasswordCheck_WhenEnabledUnset_RegistersHibpClient_OptOutDefault()
    {
        // No BreachCheck:Enabled key at all — the opt-OUT default keeps the real HIBP client wired.
        // Regressing this to opt-in (the ScbRegister idiom) is an invisible security downgrade.
        using var provider = BuildProvider(enabled: null);

        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        checker.ShouldBeOfType<HibpPasswordBreachClient>();
    }

    [Fact]
    public void AddBreachedPasswordCheck_WhenEnabledTrue_RegistersHibpClient()
    {
        using var provider = BuildProvider(enabled: true);

        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        checker.ShouldBeOfType<HibpPasswordBreachClient>();
    }

    private static ServiceProvider BuildProvider(bool? enabled)
    {
        var settings = new Dictionary<string, string?>
        {
            ["BreachCheck:BaseUrl"] = "https://api.pwnedpasswords.com/",
        };
        if (enabled.HasValue)
            settings["BreachCheck:Enabled"] = enabled.Value ? "true" : "false";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBreachedPasswordCheck(configuration);
        return services.BuildServiceProvider();
    }
}
