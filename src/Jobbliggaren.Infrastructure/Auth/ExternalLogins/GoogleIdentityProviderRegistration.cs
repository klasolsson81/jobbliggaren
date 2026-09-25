using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Registers Google as an external identity provider, or registers nothing (ADR 0142 D8, #1744 6a PR G). The gate reads
/// the client id raw: a blank value, which is what compose's <c>:-</c> passes on a host without keys, registers no
/// options, no client and no provider, and never throws. What a configured provider needs is validated when the host
/// starts, and a failure names the key, never the value.
/// </summary>
internal static class GoogleIdentityProviderRegistration
{
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);

    internal static IServiceCollection AddGoogleIdentityProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(
                configuration[$"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientId)}"]))
            return services;

        services.AddOptions<GoogleOAuthOptions>()
            .Bind(configuration.GetSection(GoogleOAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The site's one public base (ADR 0142 D8: no OAuth:RedirectBaseUrl), read raw once, so the authorization
        // request and the token request carry the same redirect_uri.
        var siteBase = configuration[$"{EmailOptions.SectionName}:{nameof(EmailOptions.BaseUrl)}"]
                       ?? new EmailOptions().BaseUrl;
        services.AddOptions<ExternalLoginRedirectOptions>()
            .Configure(options => options.SiteBaseUrl = siteBase)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalLoginRedirectOptions>, ExternalLoginRedirectOptionsValidator>();
        services.AddSingleton(sp => new ExternalLoginCallbacks(
            new Uri(sp.GetRequiredService<IOptions<ExternalLoginRedirectOptions>>().Value.SiteBaseUrl)));

        // No resilience handler: a retried token POST replays a single-use code. No redirects: a 3xx would resend the
        // body, and the body carries the client secret.
        services.AddHttpClient(GoogleIdentityProvider.HttpClientName, client => client.Timeout = ExchangeTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();
        return services;
    }
}
