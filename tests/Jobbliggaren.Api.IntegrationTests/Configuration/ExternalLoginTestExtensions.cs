using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1744 (senior-cto-advisor F11c) — Program.cs reads appsettings.Local.json in every environment, and a developer's can
/// carry a Google client with an http base, which a non-Development host refuses at start. A host whose subject is not
/// the external login calls this after its composition: no provider stays registered, and both options validate. The
/// gate itself is measured on a bare composition (GoogleIdentityProviderGateTests).
/// </summary>
internal static class ExternalLoginTestExtensions
{
    public static void NeutraliseExternalLoginsFromLocalConfiguration(this IServiceCollection services)
    {
        services.RemoveAll<IExternalIdentityProvider>();
        services.PostConfigure<GoogleOAuthOptions>(options => options.ClientSecret = "neutralised-client-secret"); // gitleaks:allow
        services.PostConfigure<ExternalLoginRedirectOptions>(options => options.SiteBaseUrl = "https://jobbliggaren.example");
    }
}
