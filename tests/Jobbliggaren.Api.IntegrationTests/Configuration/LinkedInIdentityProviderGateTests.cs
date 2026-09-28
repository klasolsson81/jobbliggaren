using System.Net;
using System.Text;
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
/// #1746 (test-writer, 6c form round §3.3) — LinkedIn's gate, measured as Google's and GitHub's are: on a bare
/// composition with in-memory configuration, because every WebApplicationFactory host reads a developer's
/// appsettings.Local.json.
/// </summary>
public class LinkedInIdentityProviderGateTests
{
    private const string ClientId = "gate-linkedin-client-id";
    private const string ClientSecret = "gate-linkedin-client-secret"; // gitleaks:allow

    private static Dictionary<string, string?> FullClient(string siteBase = "https://jobbliggaren.example") => new()
    {
        [$"{LinkedInOAuthOptions.SectionName}:{nameof(LinkedInOAuthOptions.ClientId)}"] = ClientId,
        [$"{LinkedInOAuthOptions.SectionName}:{nameof(LinkedInOAuthOptions.ClientSecret)}"] = ClientSecret,
        ["Email:BaseUrl"] = siteBase,
    };

    private static ServiceCollection Compose(string environmentName, IReadOnlyDictionary<string, string?> settings)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddLogging();
        services.AddLinkedInIdentityProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    // ── the client id decides whether anything is registered ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_client_id_registers_nothing_and_never_throws(string? clientId)
    {
        // compose passes ${AUTH_OAUTH_LINKEDIN_CLIENT_ID:-}, the empty string, on a host without keys. The base is one
        // the validator refuses, so a start that validated it anyway would throw.
        var settings = FullClient(siteBase: "http://localhost:3000");
        settings[$"{LinkedInOAuthOptions.SectionName}:{nameof(LinkedInOAuthOptions.ClientId)}"] = clientId;

        var services = Compose(Environments.Production, settings);

        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
        services.ShouldNotContain(d => d.ServiceType == typeof(IConfigureOptions<LinkedInOAuthOptions>));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginCallbacks));
        services.ShouldNotContain(d => d.ServiceType == typeof(IHttpClientFactory));
        using var provider = services.BuildServiceProvider();
        Should.NotThrow(() => provider.GetService<IStartupValidator>()?.Validate());
    }

    [Fact]
    public void A_full_client_registers_exactly_linkedin_as_a_singleton()
    {
        var services = Compose(Environments.Production, FullClient());

        var descriptor = services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider)).ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(LinkedInIdentityProvider));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IExternalIdentityProvider>().Key.ShouldBe(ExternalProviderKey.LinkedIn);
    }

    // ── the secret is validated at start, and a failure names the key ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_client_id_without_a_secret_is_refused_by_name(string? secret)
    {
        var settings = FullClient();
        settings[$"{LinkedInOAuthOptions.SectionName}:{nameof(LinkedInOAuthOptions.ClientSecret)}"] = secret;
        using var provider = Compose(Environments.Production, settings).BuildServiceProvider();

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        refusal.Message.ShouldContain(nameof(LinkedInOAuthOptions.ClientSecret));
        refusal.Message.ShouldNotContain(ClientId);
    }

    [Fact]
    public void The_options_never_print_the_secret() =>
        $"{new LinkedInOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }}".ShouldNotContain(ClientSecret);

    // ── the redirect_uri base: the rule is the shared callbacks', so the lists are Google's ──

    [Theory]
    [MemberData(nameof(GoogleIdentityProviderGateTests.AcceptedBases), MemberType = typeof(GoogleIdentityProviderGateTests))]
    public void An_accepted_base_builds_the_callback_on_the_sites_origin(string environmentName, string siteBase)
    {
        using var provider = Compose(environmentName, FullClient(siteBase)).BuildServiceProvider();

        var callback = provider.GetRequiredService<ExternalLoginCallbacks>().For(ExternalProviderKey.LinkedIn);

        callback.AbsoluteUri.ShouldBe(new Uri(new Uri(siteBase), "/api/auth/oauth/linkedin/callback").AbsoluteUri);
    }

    [Theory]
    [MemberData(nameof(GoogleIdentityProviderGateTests.RefusedBases), MemberType = typeof(GoogleIdentityProviderGateTests))]
    public void A_refused_base_fails_at_start_and_names_the_key_not_the_value(string environmentName, string siteBase)
    {
        using var provider = Compose(environmentName, FullClient(siteBase)).BuildServiceProvider();

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        refusal.Message.ShouldContain("Email:BaseUrl");
        if (siteBase.Length > 0)
            refusal.Message.ShouldNotContain(siteBase);
    }

    // ── the named client: bounded, no redirects, no retry ──

    [Fact]
    public void The_named_client_is_bounded_to_ten_seconds()
    {
        using var provider = Compose(Environments.Production, FullClient()).BuildServiceProvider();

        provider.GetRequiredService<IHttpClientFactory>().CreateClient(LinkedInIdentityProvider.HttpClientName)
            .Timeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_named_client_never_follows_a_redirect()
    {
        // A 3xx would resend the token request's body, and the body carries the client secret.
        using var provider = Compose(Environments.Production, FullClient()).BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(LinkedInIdentityProvider.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        handler.ShouldBeOfType<SocketsHttpHandler>().AllowAutoRedirect.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_request"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_redirect_uri"}""")]
    public async Task A_refused_token_request_is_sent_once(HttpStatusCode status, string body)
    {
        // The composed pipeline, with only the transport replaced: a retry would replay a single-use code. LinkedIn
        // documents its token errors under a 4xx.
        var transport = new CountingTransport(status, body);
        var services = Compose(Environments.Production, FullClient());
        services.AddHttpClient(LinkedInIdentityProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var exchange = await provider.GetRequiredService<IExternalIdentityProvider>().ExchangeAsync(
            AuthorizationCode.FromRaw("gate-linkedin-code"), PkceVerifier.Generate(), TestContext.Current.CancellationToken);

        exchange.ShouldBeOfType<ExternalExchange.Failed>();
        transport.Calls.ShouldBe(1);
    }

    private sealed class CountingTransport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
