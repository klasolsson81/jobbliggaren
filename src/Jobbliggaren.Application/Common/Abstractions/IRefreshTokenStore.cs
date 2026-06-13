namespace Jobbliggaren.Application.Common.Abstractions;

public sealed record StoredRefreshToken(
    Guid Id,
    Guid UserId,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

public interface IRefreshTokenStore
{
    Task StoreAsync(Guid userId, string tokenHash, DateTimeOffset expiresAt, string? createdByIp, CancellationToken ct);
    Task<StoredRefreshToken?> FindActiveByHashAsync(string tokenHash, CancellationToken ct);
    Task<StoredRefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAsync(Guid tokenId, Guid? replacedByTokenId, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
