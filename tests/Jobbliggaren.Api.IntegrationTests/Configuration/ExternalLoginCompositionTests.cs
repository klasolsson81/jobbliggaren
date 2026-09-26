using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1744, 6a PR S — the external-login spine is composed in the Api only, and INERT: no provider is registered,
/// whatever the configuration holds (senior-cto-advisor F1; security-auditor S4, conditions 1-2). PR G replaces the
/// inert half with the registration gate. The positive half is the control: an absence with no presence beside it
/// would pass against a build that registers nothing at all.
/// </summary>
public sealed class ExternalLoginCompositionTests
{
    // A full Google client, as Klas's appsettings.Local.json and the box's _FILE seam will carry it.
    private static IConfiguration Configuration(bool withVolatileRedis) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
            ["ConnectionStrings:Redis"] = "localhost:6379,user=api-persistent,password=synthetic",
            [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] =
                withVolatileRedis ? "localhost:6381,user=api-volatile,password=synthetic" : null,
            ["Auth:OAuth:Google:ClientId"] = "configured-client-id",
            ["Auth:OAuth:Google:ClientSecret"] = "configured-client-secret",
        }).Build();

    [Fact]
    public void The_Api_composes_the_spine_and_registers_no_provider_even_with_a_full_google_client()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(Configuration(withVolatileRedis: true));

        services.ShouldContain(d => d.ServiceType == typeof(IOAuthStateStore));
        services.ShouldContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldContain(d => d.ServiceType == typeof(IExternalLoginLookup));
        services.ShouldContain(d => d.ServiceType == typeof(IExternalLoginWriter));
        services.ShouldContain(d => d.ServiceType == typeof(ExternalLoginLinker));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
    }

    [Fact]
    public void The_Worker_composes_none_of_it()
    {
        var services = new ServiceCollection();

        services.AddCoreIdentityForWorker(Configuration(withVolatileRedis: false));

        services.ShouldNotContain(d => d.ServiceType == typeof(IOAuthStateStore));
        services.ShouldNotContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginLookup));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginWriter));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginLinker));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
    }
}
