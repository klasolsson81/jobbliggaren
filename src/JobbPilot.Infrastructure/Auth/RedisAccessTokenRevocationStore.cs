using JobbPilot.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace JobbPilot.Infrastructure.Auth;

public sealed class RedisAccessTokenRevocationStore(IDistributedCache cache)
    : IAccessTokenRevocationStore
{
    private static string Key(string jti) => $"revoked-jti:{jti}";

    public async Task RevokeAsync(string jti, TimeSpan expiresIn, CancellationToken ct)
    {
        await cache.SetStringAsync(
            Key(jti),
            "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiresIn },
            ct);
    }

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct)
    {
        var value = await cache.GetStringAsync(Key(jti), ct);
        return value is not null;
    }
}
