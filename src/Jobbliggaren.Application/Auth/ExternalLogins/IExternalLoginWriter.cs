namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>The one writer of an external login, for <see cref="ExternalLoginLinker"/> alone.</summary>
public interface IExternalLoginWriter
{
    Task<ExternalLinkResult> LinkAsync(
        Guid userId, ExternalProviderKey provider, ExternalSubject subject, CancellationToken ct);
}
