using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1744 — the external-login spine is composed in the Api only, and the Api's one wiring of the Google gate registers
/// the provider exactly when the client id is set (GoogleIdentityProviderGateTests owns the gate's own rows). The Worker
/// composes none of it, with or without a client.
/// <para>
/// #1745: GitHub's gate registers GitHub from its own client id, independent of Google's, and the Worker composes
/// neither (test-writer reading §6, the composition rows).
/// </para>
/// </summary>
public sealed class ExternalLoginCompositionTests
{
    // A full Google and GitHub client, as appsettings.Local.json and the box's _FILE seam carry them.
    private static IConfiguration Configuration(bool withVolatileRedis, bool withGoogleClient, bool withGitHubClient = false) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
            ["ConnectionStrings:Redis"] = "localhost:6379,user=api-persistent,password=synthetic",
            [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] =
                withVolatileRedis ? "localhost:6381,user=api-volatile,password=synthetic" : null,
            ["Auth:OAuth:Google:ClientId"] = withGoogleClient ? "configured-client-id" : null,
            ["Auth:OAuth:Google:ClientSecret"] = withGoogleClient ? "configured-client-secret" : null,
            ["Auth:OAuth:GitHub:ClientId"] = withGitHubClient ? "configured-github-client-id" : null,
            ["Auth:OAuth:GitHub:ClientSecret"] = withGitHubClient ? "configured-github-client-secret" : null,
        }).Build();

    private static IEnumerable<string> NamedClients(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IConfigureOptions<HttpClientFactoryOptions>))
            .Select(d => d.ImplementationInstance)
            .OfType<ConfigureNamedOptions<HttpClientFactoryOptions>>()
            .Select(options => options.Name!)
            .Distinct();

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
        services.ShouldContain(d => d.ServiceType == typeof(PendingLinkChallenge));
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
    public void The_Api_registers_both_providers_from_two_full_clients()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(
            Configuration(withVolatileRedis: true, withGoogleClient: true, withGitHubClient: true));

        services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider))
            .Select(d => d.ImplementationType)
            .ShouldBe([typeof(GoogleIdentityProvider), typeof(GitHubIdentityProvider)], ignoreOrder: true);
        services.ShouldContain(d => d.ServiceType == typeof(IConfigureOptions<GitHubOAuthOptions>));
        NamedClients(services).ShouldContain(GoogleIdentityProvider.HttpClientName);
        NamedClients(services).ShouldContain(GitHubIdentityProvider.HttpClientName);
        services.Count(d => d.ServiceType == typeof(ExternalLoginCallbacks)).ShouldBe(1);
    }

    [Fact]
    public void The_Api_registers_github_alone_from_its_own_client()
    {
        var services = new ServiceCollection();

        services.AddIdentityAndSessions(
            Configuration(withVolatileRedis: true, withGoogleClient: false, withGitHubClient: true));

        services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider)).ShouldHaveSingleItem()
            .ImplementationType.ShouldBe(typeof(GitHubIdentityProvider));
        services.ShouldNotContain(d => d.ServiceType == typeof(IConfigureOptions<GoogleOAuthOptions>));
        services.Count(d => d.ServiceType == typeof(ExternalLoginCallbacks)).ShouldBe(1);
    }

    [Fact]
    public void The_Worker_composes_none_of_it_even_with_full_clients()
    {
        var services = new ServiceCollection();

        services.AddCoreIdentityForWorker(
            Configuration(withVolatileRedis: false, withGoogleClient: true, withGitHubClient: true));

        services.ShouldNotContain(d => d.ServiceType == typeof(IOAuthStateStore));
        services.ShouldNotContain(d => d.ServiceType == typeof(RegisteredProviders));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginLookup));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalLoginWriter));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginLinker));
        services.ShouldNotContain(d => d.ServiceType == typeof(PendingLinkChallenge));
        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
        services.ShouldNotContain(d => d.ServiceType == typeof(IConfigureOptions<GitHubOAuthOptions>));
    }

    [Fact]
    public void The_shared_callbacks_are_registered_once_however_many_gates_ask()
    {
        // dotnet-architect V1, a FORM pin and said to be one: a second registration would be functionally equivalent
        // (the value and the validator are idempotent), so this pins that the site's one public base has one home and
        // a refused base is reported once, not a behaviour a double registration would change.
        var services = new ServiceCollection();
        var configuration = Configuration(withVolatileRedis: true, withGoogleClient: false);

        services.AddExternalLoginCallbacks(configuration);
        services.AddExternalLoginCallbacks(configuration);

        services.Count(d => d.ServiceType == typeof(ExternalLoginCallbacks)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IValidateOptions<ExternalLoginRedirectOptions>)).ShouldBe(1);
    }
}
