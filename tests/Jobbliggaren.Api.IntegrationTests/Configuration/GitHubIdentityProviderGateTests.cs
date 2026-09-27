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
/// #1745, 6b PR 2 (test-writer reading §6) — GitHub's gate, measured as Google's is (GoogleIdentityProviderGateTests):
/// on a bare composition with in-memory configuration, because every WebApplicationFactory host reads a developer's
/// appsettings.Local.json.
/// </summary>
public class GitHubIdentityProviderGateTests
{
    private const string ClientId = "Ov23gatefixture00000";
    private const string ClientSecret = "gate-github-client-secret"; // gitleaks:allow

    private static Dictionary<string, string?> FullClient(string siteBase = "https://jobbliggaren.example") => new()
    {
        [$"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientId)}"] = ClientId,
        [$"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientSecret)}"] = ClientSecret,
        ["Email:BaseUrl"] = siteBase,
    };

    private static ServiceCollection Compose(string environmentName, IReadOnlyDictionary<string, string?> settings)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddLogging();
        services.AddGitHubIdentityProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    // ── the client id decides whether anything is registered ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_client_id_registers_nothing_and_never_throws(string? clientId)
    {
        // compose passes ${AUTH_OAUTH_GITHUB_CLIENT_ID:-}, the empty string, on a host without keys. The base is one
        // the validator refuses, so a start that validated it anyway would throw.
        var settings = FullClient(siteBase: "http://localhost:3000");
        settings[$"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientId)}"] = clientId;

        var services = Compose(Environments.Production, settings);

        services.ShouldNotContain(d => d.ServiceType == typeof(IExternalIdentityProvider));
        services.ShouldNotContain(d => d.ServiceType == typeof(IConfigureOptions<GitHubOAuthOptions>));
        services.ShouldNotContain(d => d.ServiceType == typeof(ExternalLoginCallbacks));
        services.ShouldNotContain(d => d.ServiceType == typeof(IHttpClientFactory));
        using var provider = services.BuildServiceProvider();
        Should.NotThrow(() => provider.GetService<IStartupValidator>()?.Validate());
    }

    [Fact]
    public void A_full_client_registers_exactly_github_as_a_singleton()
    {
        var services = Compose(Environments.Production, FullClient());

        var descriptor = services.Where(d => d.ServiceType == typeof(IExternalIdentityProvider)).ShouldHaveSingleItem();
        descriptor.ImplementationType.ShouldBe(typeof(GitHubIdentityProvider));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IExternalIdentityProvider>().Key.ShouldBe(ExternalProviderKey.GitHub);
    }

    // ── the secret is validated at start, and a failure names the key ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_client_id_without_a_secret_is_refused_by_name(string? secret)
    {
        var settings = FullClient();
        settings[$"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientSecret)}"] = secret;
        using var provider = Compose(Environments.Production, settings).BuildServiceProvider();

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        refusal.Message.ShouldContain(nameof(GitHubOAuthOptions.ClientSecret));
        refusal.Message.ShouldNotContain(ClientId);
    }

    [Fact]
    public void The_options_never_print_the_secret() =>
        $"{new GitHubOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }}".ShouldNotContain(ClientSecret);

    // ── the redirect_uri base: the rule is the shared callbacks', so the lists are Google's ──

    [Theory]
    [MemberData(nameof(GoogleIdentityProviderGateTests.AcceptedBases), MemberType = typeof(GoogleIdentityProviderGateTests))]
    public void An_accepted_base_builds_the_callback_on_the_sites_origin(string environmentName, string siteBase)
    {
        using var provider = Compose(environmentName, FullClient(siteBase)).BuildServiceProvider();

        var callback = provider.GetRequiredService<ExternalLoginCallbacks>().For(ExternalProviderKey.GitHub);

        callback.AbsoluteUri.ShouldBe(new Uri(new Uri(siteBase), "/api/auth/oauth/github/callback").AbsoluteUri);
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

        provider.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubIdentityProvider.HttpClientName)
            .Timeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_named_client_never_follows_a_redirect()
    {
        // A 3xx would resend the token request's body, and the body carries the client secret.
        using var provider = Compose(Environments.Production, FullClient()).BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(GitHubIdentityProvider.HttpClientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler;

        handler.ShouldBeOfType<SocketsHttpHandler>().AllowAutoRedirect.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, """{"error":"bad_verification_code"}""")]
    public async Task A_refused_token_request_is_sent_once(HttpStatusCode status, string body)
    {
        // The composed pipeline, with only the transport replaced: a retry would replay a single-use code. GitHub
        // documents its token errors under 200 too. No token arrives, so nothing is revoked either.
        var transport = new CountingTransport(status, body);
        var services = Compose(Environments.Production, FullClient());
        services.AddHttpClient(GitHubIdentityProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var exchange = await provider.GetRequiredService<IExternalIdentityProvider>().ExchangeAsync(
            AuthorizationCode.FromRaw("gate-github-code"), PkceVerifier.Generate(), TestContext.Current.CancellationToken);

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
