using JobbPilot.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace JobbPilot.Infrastructure.Auth;

#pragma warning disable JOBBPILOT0001 // Tas bort i Fas 1, ADR 0017
[Obsolete(
    "JWT-revokering ersätts av ISessionStore.InvalidateAsync i Fas 0 STEG 4b (ADR 0017). " +
    "Klassen bevaras tillfälligt för RefreshCommandHandler. Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBPILOT0001",
    UrlFormat = "https://github.com/klasolsson81/jobbpilot/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
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
#pragma warning restore JOBBPILOT0001
