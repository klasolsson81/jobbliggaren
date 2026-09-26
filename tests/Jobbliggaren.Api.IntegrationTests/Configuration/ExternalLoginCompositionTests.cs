using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1744 — the external-login spine is composed in the Api only, and the Api's one wiring of the Google gate registers
/// the provider exactly when the client id is set (GoogleIdentityProviderGateTests owns the gate's own rows). The Worker
/// composes none of it, with or without a client.
/// </summary>
public sealed class ExternalLoginCompositionTests
{
    // A full Google client, as appsettings.Local.json and the box's _FILE seam carry it.
    private static IConfiguration Configuration(bool withVolatileRedis, bool withGoogleClient) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
            ["ConnectionStrings:Redis"] = "localhost:6379,user=api-persistent,password=synthetic",
            [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] =
                withVolatileRedis ? "localhost:6381,user=api-volatile,password=synthetic" : null,
            ["Auth:OAuth:Google:ClientId"] = withGoogleClient ? "configured-client-id" : null,
            ["Auth:OAuth:Google:ClientSecret"] = withGoogleClient ? "configured-client-secret" : null,
        }).Build();

    [Fact]
    public void The_Api_composes_the_spine_and_registers_google_from_a_full_client()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(Configuration(withVolatileRedis: true, withGoogleClient: true));

        services.ShouldContain(d => d.ServiceType == typeof(IOAuthStateStore));
        services.ShouldContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldContain(d => d.ServiceType == typeof(IExternalLoginLookup));
        services.ShouldContain(d => d.ServiceType == typeof(IExternalLoginWriter));
        services.ShouldContain(d => d.ServiceType == typeof(ExternalLoginLinker));
        services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider)).ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(GoogleIdentityProvider));
    }

    [Fact]
    public void The_Api_composes_the_spine_and_no_provider_without_a_client()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(Configuration(withVolatileRedis: true, withGoogleClient: false));

        services.ShouldContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
    }

    [Fact]
    public void The_Worker_composes_none_of_it_even_with_a_full_client()
    {
        var services = new ServiceCollection();

        services.AddCoreIdentityForWorker(Configuration(withVolatileRedis: false, withGoogleClient: true));

        services.ShouldNotContain(d => d.ServiceType == typeof(IOAuthStateStore));
        services.ShouldNotContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginLookup));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginWriter));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginLinker));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
    }
}
