using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// The callback URL the web serves for each provider, built from the site's one public base URL (ADR 0142 D8: no
/// <c>OAuth:RedirectBaseUrl</c>). Built once, so the authorization request and the token request always carry the
/// same <c>redirect_uri</c>, which the provider requires to match exactly.
/// </summary>
internal sealed class ExternalLoginCallbacks(Uri siteBase)
{
    /// <summary>The web route handler's path. The provider consoles register this path on every host.</summary>
    internal static string PathFor(ExternalProviderKey key) => $"/api/auth/oauth/{key.Value}/callback";

    public Uri For(ExternalProviderKey key) => new(siteBase, PathFor(key));
}
