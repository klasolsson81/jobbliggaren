using System.Net;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1744, 6a PR G (senior-cto-advisor F11c; security-auditor question 10 and m-4; test-writer Majors 2 and 6) — the one
/// gate a provider passes before it exists on a host. Measured on a bare composition with in-memory configuration:
/// every WebApplicationFactory host reads a developer's appsettings.Local.json, so the empty-list half cannot be
/// measured honestly on one (test-writer Major 2).
/// </summary>
public class GoogleIdentityProviderGateTests
{
    private const string ClientId = "gate-client-id.apps.googleusercontent.com";
    private const string ClientSecret = "gate-client-secret"; // gitleaks:allow

    private static Dictionary<string, string?> FullClient(string siteBase = "https://jobbliggaren.example") => new()
    {
        [$"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientId)}"] = ClientId,
        [$"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientSecret)}"] = ClientSecret,
        ["Email:BaseUrl"] = siteBase,
    };

    private static ServiceCollection Compose(string environmentName, IReadOnlyDictionary<string, string?> settings)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddLogging();
        services.AddGoogleIdentityProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    // ── the client id decides whether anything is registered ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_client_id_registers_nothing_and_never_throws(string? clientId)
    {
        // compose passes ${AUTH_OAUTH_GOOGLE_CLIENT_ID:-}, the empty string, on a host without keys.
        var settings = FullClient();
        settings[$"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientId)}"] = clientId;

        var services = Compose(Environments.Production, settings);

        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
        services.ShouldNotContain(d => d.ServiceType == typeof(IConfigureOptions<GoogleOAuthOptions>));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginCallbacks));
        services.ShouldNotContain(d => d.ServiceType == typeof(IHttpClientFactory));
    }

    [Fact]
    public void A_full_client_registers_exactly_google_as_a_singleton()
    {
        var services = Compose(Environments.Production, FullClient());

        var descriptor = services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider)).ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(GoogleIdentityProvider));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IExternalIdentityProvider>().Key.ShouldBe(ExternalProviderKey.Google);
    }

    // ── the secret is validated at start, and a failure names the key ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_client_id_without_a_secret_is_refused_by_name(string? secret)
    {
        var settings = FullClient();
        settings[$"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientSecret)}"] = secret;
        using var provider = Compose(Environments.Production, settings).BuildServiceProvider();

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GoogleOAuthOptions>>().Value);

        refusal.Message.ShouldContain(nameof(GoogleOAuthOptions.ClientSecret));
        refusal.Message.ShouldNotContain(ClientId);
    }

    [Fact]
    public void The_options_never_print_the_secret() =>
        $"{new GoogleOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }}".ShouldNotContain(ClientSecret);

    // ── the redirect_uri base ──

    public static TheoryData<string, string> AcceptedBases => new()
    {
        { Environments.Production, "https://jobbliggaren.se" },
        { Environments.Production, "https://dev.jobbliggaren.se/" },
        { Environments.Development, "https://localhost:3000" },
        { Environments.Development, "http://localhost:3000" },
        { Environments.Development, "http://127.0.0.1:3000" },
    };

    [Theory]
    [MemberData(nameof(AcceptedBases))]
    public void An_accepted_base_builds_the_callback_on_the_sites_origin(string environmentName, string siteBase)
    {
        using var provider = Compose(environmentName, FullClient(siteBase)).BuildServiceProvider();

        var callback = provider.GetRequiredService<ExternalLoginCallbacks>().For(ExternalProviderKey.Google);

        callback.AbsoluteUri.ShouldBe(new Uri(new Uri(siteBase), "/api/auth/oauth/google/callback").AbsoluteUri);
    }

    public static TheoryData<string, string> RefusedBases => new()
    {
        { Environments.Production, "http://localhost:3000" },
        { Environments.Production, "http://jobbliggaren.se" },
        { Environments.Staging, "http://localhost:3000" },
        { Environments.Development, "http://dev.jobbliggaren.se" },
        { Environments.Development, "http://192.0.2.10:3000" },
        { Environments.Production, "https://jobbliggaren.se/?next=x" },
        { Environments.Production, "https://jobbliggaren.se/#x" },
        { Environments.Production, "https://user:pw@jobbliggaren.se" },
        { Environments.Production, "/relative" },
        { Environments.Production, "" },
    };

    [Theory]
    [MemberData(nameof(RefusedBases))]
    public void A_refused_base_fails_at_start_and_names_the_key_not_the_value(string environmentName, string siteBase)
    {
        using var provider = Compose(environmentName, FullClient(siteBase)).BuildServiceProvider();

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ExternalLoginRedirectOptions>>().Value);

        refusal.Message.ShouldContain("Email:BaseUrl");
        if (siteBase.Length > 0)
            refusal.Message.ShouldNotContain(siteBase);
    }

    [Fact]
    public void Without_a_base_the_default_is_refused_outside_development()
    {
        var settings = FullClient();
        settings.Remove("Email:BaseUrl");
        using var provider = Compose(Environments.Production, settings).BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ExternalLoginRedirectOptions>>().Value);
    }

    // ── the named client: bounded, no redirects, no retry ──

    [Fact]
    public void The_named_client_is_bounded_to_ten_seconds()
    {
        using var provider = Compose(Environments.Production, FullClient()).BuildServiceProvider();

        provider.GetRequiredService<IHttpClientFactory>().CreateClient(GoogleIdentityProvider.HttpClientName)
            .Timeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_named_client_never_follows_a_redirect()
    {
        // A 3xx would resend the token request's body, and the body carries the client secret.
        using var provider = Compose(Environments.Production, FullClient()).BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(GoogleIdentityProvider.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        handler.ShouldBeOfType<SocketsHttpHandler>().AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public async Task A_refused_token_request_is_sent_once()
    {
        // The composed pipeline, with only the transport replaced: a retry would replay a single-use code.
        var transport = new CountingTransport(HttpStatusCode.ServiceUnavailable);
        var services = Compose(Environments.Production, FullClient());
        services.AddHttpClient(GoogleIdentityProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var identity = await provider.GetRequiredService<IExternalIdentityProvider>().ExchangeAsync(
            AuthorizationCode.FromRaw("4/0AVGzR1gate"), PkceVerifier.Generate(), TestContext.Current.CancellationToken);

        identity.ShouldBeNull();
        transport.Calls.ShouldBe(1);
    }

    private sealed class CountingTransport(HttpStatusCode status) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
