using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// What every provider's gate shares (ADR 0142 D8, #1745). Each gate calls these only after its own client id is
/// read, so a host without keys still registers nothing; the callbacks are registered once however many gates open,
/// so the site's one public base has one home and a refused base is reported once.
/// </summary>
internal static class ExternalLoginRegistration
{
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Every provider's gate, in the order the login page lists them. It registers no provider itself.</summary>
    internal static IServiceCollection AddExternalIdentityProviders(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddGoogleIdentityProvider(configuration)
            .AddGitHubIdentityProvider(configuration);

    internal static IServiceCollection AddExternalLoginCallbacks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(ExternalLoginCallbacks)))
            return services;

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
        return services;
    }

    /// <summary>
    /// A provider's named client. No resilience handler: a retried token POST replays a single-use code. No redirects:
    /// a 3xx would resend the body, and the body carries the client secret.
    /// </summary>
    internal static IHttpClientBuilder AddExternalLoginHttpClient(this IServiceCollection services, string name) =>
        services.AddHttpClient(name, client => client.Timeout = ExchangeTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
}
