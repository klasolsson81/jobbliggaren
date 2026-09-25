namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The providers this host registered (ADR 0142 D8): a provider without keys is not registered, so the registered
/// set IS what the login page may offer. Known order, so the list reads the same on every host.
/// </summary>
public sealed class RegisteredProviders(IEnumerable<IExternalIdentityProvider> providers)
{
    private readonly IReadOnlyList<IExternalIdentityProvider> _providers = providers.ToList();

    /// <summary>The registered provider under a raw key, or null for an unknown key and for a known one not registered.</summary>
    public IExternalIdentityProvider? Find(string? rawKey) =>
        ExternalProviderKey.TryParse(rawKey, out var key) ? _providers.FirstOrDefault(p => p.Key == key) : null;

    public IReadOnlyList<ExternalProviderKey> Keys =>
        ExternalProviderKey.Known.Where(key => _providers.Any(p => p.Key == key)).ToList();
}
