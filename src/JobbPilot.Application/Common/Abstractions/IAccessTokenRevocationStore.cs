namespace JobbPilot.Application.Common.Abstractions;

public interface IAccessTokenRevocationStore
{
    Task RevokeAsync(string jti, TimeSpan expiresIn, CancellationToken ct);
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct);
}
