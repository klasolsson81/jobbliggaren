using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Registers LinkedIn as an external identity provider, or registers nothing (ADR 0142 D8, #1746), by the same gate as
/// Google's and GitHub's: the client id is read raw, a blank one registers nothing and never throws, and a configured
/// client is validated when the host starts, naming the key and never the value.
/// </summary>
internal static class LinkedInIdentityProviderRegistration
{
    internal static IServiceCollection AddLinkedInIdentityProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(
                configuration[$"{LinkedInOAuthOptions.SectionName}:{nameof(LinkedInOAuthOptions.ClientId)}"]))
            return services;

        services.AddOptions<LinkedInOAuthOptions>()
            .Bind(configuration.GetSection(LinkedInOAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddExternalLoginCallbacks(configuration);
        services.AddExternalLoginHttpClient(LinkedInIdentityProvider.HttpClientName);

        services.AddSingleton<IExternalIdentityProvider, LinkedInIdentityProvider>();
        return services;
    }
}
