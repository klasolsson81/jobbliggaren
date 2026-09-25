namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// The base every provider's <c>redirect_uri</c> is built on: the raw <c>Email:BaseUrl</c>, captured when a provider
/// is registered and validated at start by <see cref="ExternalLoginRedirectOptionsValidator"/>.
/// </summary>
internal sealed class ExternalLoginRedirectOptions
{
    public string SiteBaseUrl { get; set; } = string.Empty;
}
