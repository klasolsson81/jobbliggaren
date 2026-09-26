namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// Which account an external login is linked to, for <c>LoginSubjectResolver</c> alone: the one place that
/// classifies a login subject also answers who a provider's identifier belongs to.
/// </summary>
public interface IExternalLoginLookup
{
    Task<Guid?> FindUserIdAsync(ExternalProviderKey provider, ExternalSubject subject, CancellationToken ct);
}
