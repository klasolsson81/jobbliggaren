using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.ExternalLogins;

/// <summary>
/// Refuses a <c>redirect_uri</c> base a provider would not accept, or should not be sent to (ADR 0142 D8): absolute,
/// no user info, query or fragment, and https. Development alone may use http, and then only to a loopback host, so
/// a misconfigured box can never send live codes to a developer's machine. The messages name the key, never the value.
/// </summary>
internal sealed class ExternalLoginRedirectOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<ExternalLoginRedirectOptions>
{
    private const string Key = $"{EmailOptions.SectionName}:{nameof(EmailOptions.BaseUrl)}";

    public ValidateOptionsResult Validate(string? name, ExternalLoginRedirectOptions options)
    {
        if (!Uri.TryCreate(options.SiteBaseUrl, UriKind.Absolute, out var site)
            || site.UserInfo.Length > 0 || site.Query.Length > 0 || site.Fragment.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"{Key} must be an absolute URL without user info, a query or a fragment when an external login is configured.");
        }

        if (site.Scheme == Uri.UriSchemeHttps
            || (site.Scheme == Uri.UriSchemeHttp && site.IsLoopback && environment.IsDevelopment()))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{Key} must be https when an external login is configured; http is accepted only for a loopback host in Development.");
    }
}
