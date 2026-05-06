using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JobbPilot.Infrastructure.Auth.Sessions;

public sealed class RedisSessionStore(
    IDistributedCache cache,
    IDateTimeProvider dateTimeProvider,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    private readonly TimeSpan _ttl = options.Value.Ttl;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct)
    {
        string? json;
        try
        {
            // Timing-säkerhet: Redis GET är hash-tabell-uppslagning, inte byte-jämförelse.
            // 256-bit session-id-entropi gör enumeration via timing oexploaterbar.
            json = await cache.GetStringAsync(Key(sessionId), ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        if (json is null) return null;

        var payload = JsonSerializer.Deserialize<SessionPayload>(json, JsonOptions);
        if (payload is null) return null;

        var expiresAt = dateTimeProvider.UtcNow + _ttl;

        // Reset sliding expiration on read
        try
        {
            await cache.SetStringAsync(
                Key(sessionId),
                json,
                new DistributedCacheEntryOptions { SlidingExpiration = _ttl },
                ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        return new Session(sessionId, payload.UserId, payload.CreatedAt, expiresAt);
    }

    public async Task<Session> CreateAsync(Guid userId, CancellationToken ct)
    {
        var sessionId = SessionId.Generate();
        var now = dateTimeProvider.UtcNow;
        var expiresAt = now + _ttl;

        var payload = new SessionPayload(userId, now);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        try
        {
            // TODO Fas 1: secondary index for efficient InvalidateAllForUserAsync
            // (GDPR erasure via SCAN until then)
            await cache.SetStringAsync(
                Key(sessionId),
                json,
                new DistributedCacheEntryOptions { SlidingExpiration = _ttl },
                ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        return new Session(sessionId, userId, now, expiresAt);
    }

    public async Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct)
    {
        string? existing;
        try
        {
            existing = await cache.GetStringAsync(Key(sessionId), ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        if (existing is null) return false;

        try
        {
            // TODO Fas 1: secondary index for efficient InvalidateAllForUserAsync
            // (GDPR erasure via SCAN until then)
            await cache.RemoveAsync(Key(sessionId), ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        return true;
    }

    // Session-id hashas med SHA-256 → base64url innan det används som Redis-nyckel.
    // Skyddar mot Redis-dump-läckage: raw token aldrig synligt i Redis.
    // (jobbpilot:-prefixet läggs till automatiskt av IDistributedCache-konfigurationen)
    private static string Key(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        return $"session:{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    private sealed record SessionPayload(Guid UserId, DateTimeOffset CreatedAt);
}
