using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1745 (test-writer Major 6) — the neutraliser every non-login Production host calls, measured against each gate: a
/// developer's appsettings.Local.json can carry any provider's client id without its secret and with an http base, and
/// a gate the neutraliser missed would fail those hosts on that machine alone while CI stays green.
/// </summary>
public class ExternalLoginNeutraliserTests
{
    public static TheoryData<string> Gates => new(GoogleOAuthOptions.SectionName, GitHubOAuthOptions.SectionName);

    private static ServiceCollection ComposeWithAClientIdAlone(string section)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddLogging();
        services.AddExternalIdentityProviders(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{section}:ClientId"] = "a-developers-client-id",
                ["Email:BaseUrl"] = "http://localhost:3000",
            }).Build());
        return services;
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public void Without_the_neutraliser_the_gate_refuses_the_start(string section)
    {
        // The control: the row below would pass vacuously on a configuration the gate never registered. Both refusals
        // the neutraliser answers are here, the missing secret and the http base.
        using var provider = ComposeWithAClientIdAlone(section).BuildServiceProvider();

        var refusals = Should.Throw<AggregateException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .InnerExceptions;

        refusals.ShouldAllBe(refusal => refusal is OptionsValidationException);
        refusals.ShouldContain(refusal => refusal.Message.Contains("ClientSecret"));
        refusals.ShouldContain(refusal => refusal.Message.Contains("Email:BaseUrl"));
    }

    [Theory]
    [MemberData(nameof(Gates))]
    public void The_neutraliser_leaves_no_provider_and_a_start_that_validates(string section)
    {
        var services = ComposeWithAClientIdAlone(section);

        services.NeutraliseExternalLoginsFromLocalConfiguration();

        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
        using var provider = services.BuildServiceProvider();
        Should.NotThrow(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }
}
