using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Registers Google as an external identity provider, or registers nothing (ADR 0142 D8, #1744 6a PR G). The gate reads
/// the client id raw: a blank value, which is what compose's <c>:-</c> passes on a host without keys, registers no
/// options, no client and no provider, and never throws. What a configured provider needs is validated when the host
/// starts, and a failure names the key, never the value.
/// </summary>
internal static class GoogleIdentityProviderRegistration
{
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

        services.AddExternalLoginCallbacks(configuration);
        services.AddExternalLoginHttpClient(GoogleIdentityProvider.HttpClientName);

        services.AddSingleton<IExternalIdentityProvider, GoogleIdentityProvider>();
        return services;
    }
}
