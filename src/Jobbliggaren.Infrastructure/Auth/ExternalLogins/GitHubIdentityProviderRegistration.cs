using Jobbliggaren.Application.Auth.ExternalLogins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Registers GitHub as an external identity provider, or registers nothing (ADR 0142 D8, #1745), by the same gate as
/// Google's: the client id is read raw, a blank one registers nothing and never throws, and a configured client is
/// validated when the host starts, naming the key and never the value.
/// </summary>
internal static class GitHubIdentityProviderRegistration
{
    internal static IServiceCollection AddGitHubIdentityProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(
                configuration[$"{GitHubOAuthOptions.SectionName}:{nameof(GitHubOAuthOptions.ClientId)}"]))
            return services;

        services.AddOptions<GitHubOAuthOptions>()
            .Bind(configuration.GetSection(GitHubOAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddExternalLoginCallbacks(configuration);
        services.AddExternalLoginHttpClient(GitHubIdentityProvider.HttpClientName);

        services.AddSingleton<IExternalIdentityProvider, GitHubIdentityProvider>();
        return services;
    }
}
