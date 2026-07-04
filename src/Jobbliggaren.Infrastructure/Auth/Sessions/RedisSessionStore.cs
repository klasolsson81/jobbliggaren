using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class RedisSessionStore(
    IDistributedCache cache,
    IConnectionMultiplexer redis,
    IDateTimeProvider dateTimeProvider,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    // IDistributedCache prefixar automatiskt med "jobbliggaren:" (InstanceName).
    // För secondary index måste vi prefixa manuellt eftersom vi använder
    // IConnectionMultiplexer direkt — håll prefixet identiskt.
    private const string KeyPrefix = "jobbliggaren:";

    private readonly TimeSpan _slidingTtl = options.Value.SlidingTtl;
    private readonly TimeSpan _absoluteTtl = options.Value.AbsoluteTtl;

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

        var now = dateTimeProvider.UtcNow;

        // Absolute lifetime cap (#481 Low): a session may not outlive CreatedAt +
        // AbsoluteTtl, however actively it is used. Past the cap, evict it (drops the
        // main key AND its user-index membership via InvalidateAsync) and treat it as
        // gone, so the auth handler rejects the request. CreatedAt is the sole anchor
        // — it is never rewritten, so the cap cannot be reset by continued use.
        // Inclusive (>=): at exactly the ceiling the session is already spent, and it
        // guarantees capRemaining is strictly positive below (a zero SlidingExpiration
        // throws).
        if (now - payload.CreatedAt >= _absoluteTtl)
        {
            await InvalidateAsync(sessionId, ct);
            return null;
        }

        // Slide up to SlidingTtl, but never past the absolute cap: a key's TTL must
        // not outlive the cap (defense-in-depth beside the check above, and it frees
        // Redis memory promptly at the ceiling).
        var capRemaining = payload.CreatedAt + _absoluteTtl - now;
        var slidingTtl = capRemaining < _slidingTtl ? capRemaining : _slidingTtl;
        var expiresAt = now + slidingTtl;
        var sessionKey = Key(sessionId);

        // Reset sliding expiration on read — for BOTH the secondary user-sessions-
        // index AND the main session key, so the index never dies before the
        // session it tracks (#502 / ADR 0024 D4). CreateAsync sets the set-TTL once
        // at login; without refreshing it here the set expires SlidingTtl after the LAST
        // login while GetAsync slides the main key forward indefinitely →
        // InvalidateAllForUserAsync iterates an empty set and the token keeps
        // authenticating after account deletion (Art. 17). Re-SADD is required (not
        // just KeyExpire): if the set already expired, KeyExpire on the gone key is
        // a no-op — the membership must be recreated. Order mirrors CreateAsync
        // (SADD before KeyExpire, sequentially awaited) so a reordered KeyExpire can
        // never set a TTL on a not-yet-created set and leave it immortal. Non-atomic,
        // same accepted worst-case as CreateAsync (TD-23 — now a third such site).
        try
        {
            var db = redis.GetDatabase();
            var setKey = UserSessionsKey(payload.UserId);
            await db.SetAddAsync(setKey, sessionKey);
            // The index SET keeps the full sliding window (it must outlive the sessions
            // it tracks, #502); a member whose capped main key has expired is a benign
            // orphan (RemoveAsync is a no-op on it).
            await db.KeyExpireAsync(setKey, _slidingTtl);

            await cache.SetStringAsync(
                sessionKey,
                json,
                new DistributedCacheEntryOptions { SlidingExpiration = slidingTtl },
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
        // Fresh session: CreatedAt == now, so the absolute cap (>= SlidingTtl) never
        // binds tighter than the sliding window at creation.
        var expiresAt = now + _slidingTtl;

        var payload = new SessionPayload(userId, now);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var sessionKey = Key(sessionId);

        try
        {
            // Secondary user-sessions-index FÖRST (ADR 0024 D4 + säkerhets-
            // hardening per security-auditor Sec-Minor-3): SADD session-key
            // i SET före main-key skapas. Skälet: om vi gör main-key först
            // och SADD failer (Redis-connection-fel) får vi en aktiv session
            // som inte ligger i secondary-index → InvalidateAllForUserAsync
            // missar den vid kontoradering = SÄKERHETSHÅL. Med SADD-först
            // blir worst-case: orphan-membership i set om main-key-SET failer,
            // vilket ger no-op vid InvalidateAllForUserAsync (cache.RemoveAsync
            // på icke-existerande key är säker). Slutgiltig atomisk garanti
            // kräver MULTI/EXEC eller Lua-script (TD-23).
            //
            // Vi lagrar IDistributedCache-key:n (samma hash som main cache-key
            // utan prefix) så InvalidateAllForUserAsync kan anropa
            // cache.RemoveAsync(member) direkt utan extra hash-runda.
            // Set-key:n får TTL = sliding-fönstret (förlängs vid varje create);
            // expirerar tillsammans med användarens sista session.
            var db = redis.GetDatabase();
            var setKey = UserSessionsKey(userId);
            await db.SetAddAsync(setKey, sessionKey);
            await db.KeyExpireAsync(setKey, _slidingTtl);

            // Primär session-rad efter SADD
            await cache.SetStringAsync(
                sessionKey,
                json,
                new DistributedCacheEntryOptions { SlidingExpiration = _slidingTtl },
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

        // Hämta payload för att veta vilken user:s set vi ska SREM från.
        // Om payload-deserialiseringen misslyckas (korrupt data) hoppar vi
        // bara secondary-index-borttagning — main-key:n droppas ändå.
        var payload = JsonSerializer.Deserialize<SessionPayload>(existing, JsonOptions);

        try
        {
            if (payload is not null)
            {
                var db = redis.GetDatabase();
                await db.SetRemoveAsync(UserSessionsKey(payload.UserId), Key(sessionId));
            }

            await cache.RemoveAsync(Key(sessionId), ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        return true;
    }

    public async Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct)
    {
        // ADR 0024 D4 + ADR 0017 deferred — bulk-invalidering vid kontoradering.
        // Iterera secondary-index, droppa varje session-key, droppa setet självt.
        // O(N) över användarens aktiva sessioner — typiskt 1-3 i Fas 1.
        try
        {
            var db = redis.GetDatabase();
            var setKey = UserSessionsKey(userId);

            var members = await db.SetMembersAsync(setKey);
            var count = 0;
            foreach (var member in members)
            {
                var sessionKey = (string?)member;
                if (sessionKey is null) continue;
                await cache.RemoveAsync(sessionKey, ct);
                count++;
            }

            await db.KeyDeleteAsync(setKey);
            return count;
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }
    }

    // Session-id hashas med SHA-256 → base64url innan det används som Redis-nyckel.
    // Skyddar mot Redis-dump-läckage: raw token aldrig synligt i Redis.
    // (jobbliggaren:-prefixet läggs till automatiskt av IDistributedCache-konfigurationen)
    private static string Key(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        return $"session:{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    // Secondary-index-key: tracks alla aktiva session-keys för en user.
    // Manuellt prefixad med jobbliggaren: eftersom vi använder IConnectionMultiplexer
    // direkt (inte IDistributedCache som auto-prefixar via InstanceName).
    private static string UserSessionsKey(Guid userId) =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}user:{userId}:sessions");

    private sealed record SessionPayload(Guid UserId, DateTimeOffset CreatedAt);
}
