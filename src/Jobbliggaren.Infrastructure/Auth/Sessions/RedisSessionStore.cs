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

    private readonly SessionStoreOptions _options = options.Value;

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

        // The session keeps the lifetime profile it was created under (persisted in the
        // payload). A pre-profiles payload has no Lifetime field → deserializes to
        // Legacy (ordinal 0) → today's reach, so no session breaks on the deploy.
        var profile = _options.ProfileFor(payload.Lifetime);

        // Absolute lifetime cap (#481 Low): a session may not outlive CreatedAt +
        // AbsoluteTtl, however actively it is used. Past the cap, evict it (drops the
        // main key AND its user-index membership via InvalidateAsync) and treat it as
        // gone, so the auth handler rejects the request. CreatedAt is the sole anchor
        // — it is never rewritten, so the cap cannot be reset by continued use.
        // Inclusive (>=): at exactly the ceiling the session is already spent, and it
        // guarantees capRemaining is strictly positive below (a zero SlidingExpiration
        // throws).
        if (now - payload.CreatedAt >= profile.AbsoluteTtl)
        {
            await InvalidateAsync(sessionId, ct);
            return null;
        }

        // COND-A grace: a superseded key (rotated away, living out its fixed grace window)
        // still authenticates so concurrent in-flight requests bearing the old id don't 401
        // — but it must NOT be slid (SetStringAsync would reset the fixed AbsoluteExpiration
        // and defeat the bounded window; a "naive KeyExpire grace" fails for exactly this
        // reason) and must NOT be re-SADDed (its index membership was intentionally dropped
        // on rotation). Return it as-is; it dies at its AbsoluteExpiration. The absolute-cap
        // check above stays ahead, so a superseded-but-past-cap key is still evicted.
        if (payload.SupersededAt is { } supersededAt)
            return new Session(
                sessionId, payload.UserId, payload.CreatedAt, supersededAt + _options.RotationGraceWindow);

        // Slide up to SlidingTtl, but never past the absolute cap: a key's TTL must
        // not outlive the cap (defense-in-depth beside the check above, and it frees
        // Redis memory promptly at the ceiling).
        var capRemaining = payload.CreatedAt + profile.AbsoluteTtl - now;
        var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;
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
            await db.KeyExpireAsync(setKey, profile.SlidingTtl);

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

    public async Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct)
    {
        var sessionId = SessionId.Generate();
        var now = dateTimeProvider.UtcNow;
        var profile = _options.ProfileFor(lifetime);
        // Fresh session: CreatedAt == now, so the absolute cap (>= SlidingTtl) never
        // binds tighter than the sliding window at creation. RotatedAt starts at
        // CreatedAt (unused until the rotation seam ships in 2b).
        var expiresAt = now + profile.SlidingTtl;

        var payload = new SessionPayload(userId, now, now, lifetime);
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
            await db.KeyExpireAsync(setKey, profile.SlidingTtl);

            // Primär session-rad efter SADD
            await cache.SetStringAsync(
                sessionKey,
                json,
                new DistributedCacheEntryOptions { SlidingExpiration = profile.SlidingTtl },
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

            // COND-B: plant the revocation tombstone BEFORE snapshotting the index, so any
            // concurrent RotateAsync whose new key lands after our SMEMBERS snapshot observes
            // it and fails closed (undoes itself). TTL comfortably exceeds a single rotation's
            // runtime; it self-expires so it never blocks a later legitimate login.
            await db.StringSetAsync(UserRevokedKey(userId), RevokedValue, _options.RevocationTombstoneTtl);

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

    public async Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct)
    {
        string? json;
        try
        {
            json = await cache.GetStringAsync(Key(current), ct);
        }
        catch (RedisConnectionException ex)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }

        if (json is null) return null;

        var payload = JsonSerializer.Deserialize<SessionPayload>(json, JsonOptions);
        if (payload is null) return null;

        // A superseded key is already rotated away and living out its grace window — never
        // rotate it again (its successor is the live id; rotating it would fork the chain).
        if (payload.SupersededAt is not null) return null;

        var profile = _options.ProfileFor(payload.Lifetime);
        if (profile.RotationInterval <= TimeSpan.Zero) return null; // profile never rotates

        var now = dateTimeProvider.UtcNow;

        // Defensive: never rotate a session already past its absolute cap (GetAsync would
        // have evicted it first on the refresh request, but RotateAsync is public).
        if (now - payload.CreatedAt >= profile.AbsoluteTtl) return null;

        // Interval-gate: rotate only once RotationInterval has elapsed since the last
        // rotation (or, for a never-rotated session, since it was created — a legacy
        // payload with RotatedAt == default anchors on CreatedAt).
        var rotatedAt = payload.RotatedAt == default ? payload.CreatedAt : payload.RotatedAt;
        if (now - rotatedAt < profile.RotationInterval) return null;

        try
        {
            var db = redis.GetDatabase();

            // Single-winner election: of a concurrent burst of refresh requests, exactly
            // one may rotate. SET NX on a short-lived claim key; a loser returns null and
            // keeps using `current` (still valid). The claim self-expires, so a crash
            // between claim and rotation just defers rotation to the next interval.
            if (!await db.StringSetAsync(
                    RotationClaimKey(current), RotationClaimValue, _options.RotationClaimTtl, When.NotExists))
                return null;

            var newId = SessionId.Generate();
            var newKey = Key(newId);
            var setKey = UserSessionsKey(payload.UserId);

            // Preserve CreatedAt + Lifetime verbatim (the cap anchor must never reset);
            // stamp RotatedAt = now.
            var rotatedPayload = payload with { RotatedAt = now };
            var rotatedJson = JsonSerializer.Serialize(rotatedPayload, JsonOptions);

            // Slide the new key up to SlidingTtl, clamped to the absolute cap (as GetAsync).
            var capRemaining = payload.CreatedAt + profile.AbsoluteTtl - now;
            var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;

            // New index member FIRST (mirror of CreateAsync) so InvalidateAllForUserAsync
            // can never miss the rotated session during the transition; the old member is
            // removed after, leaving at worst a benign transient double-membership.
            await db.SetAddAsync(setKey, newKey);
            await cache.SetStringAsync(
                newKey,
                rotatedJson,
                new DistributedCacheEntryOptions { SlidingExpiration = slidingTtl },
                ct);
            await db.KeyExpireAsync(setKey, profile.SlidingTtl);

            // COND-A grace: retire the old id into a bounded, non-sliding grace window instead
            // of hard-deleting it, so concurrent in-flight requests still bearing the old id
            // (parallel fetches, other tabs, a rotation-loser's render) authenticate for up to
            // RotationGraceWindow rather than 401-ing into a spurious logout. Overwrite the old
            // key with a superseded marker on a FIXED AbsoluteExpiration (never slid — GetAsync
            // short-circuits it, so a "naive KeyExpire grace" re-slid by GetAsync is avoided),
            // and LEAVE it in the user index so InvalidateAllForUserAsync still reaches it
            // (Art. 17): it either self-expires at the grace ceiling (leaving a benign orphan
            // member, exactly as GetAsync/CreateAsync already do) or is killed by a revoke.
            var supersededPayload = payload with { RotatedAt = now, SupersededAt = now };
            var supersededJson = JsonSerializer.Serialize(supersededPayload, JsonOptions);
            await cache.SetStringAsync(
                Key(current),
                supersededJson,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _options.RotationGraceWindow },
                ct);

            // COND-B fail-closed: a concurrent InvalidateAllForUserAsync (account deletion /
            // logout-everywhere) plants a per-user revocation tombstone BEFORE it snapshots the
            // index. This check is the LAST operation of the rotation — placed AFTER *both* the
            // new-key write (SADD+SetString) AND the superseded-old write above — so it gates
            // every mutation this rotation made. If the tombstone is present, undo the whole
            // rotation (drop new AND the just-superseded old, index + cache) and return null;
            // the caller gets rotated:false, keeps `current`, and its next read 401s once the
            // revoke completes. The happens-before is then symmetric: if this check sees no
            // tombstone it ran before the revoke's plant, so ALL our writes preceded the revoke's
            // snapshot and that snapshot sees + removes both keys. No write can survive past the
            // check, so the superseded old key can never be resurrected outside the index — the
            // ≤grace Art. 17 resurrection race is closed (mirrors InMemory, whose writes also all
            // precede its IsRevoked check).
            if (await db.KeyExistsAsync(UserRevokedKey(payload.UserId)))
            {
                await db.SetRemoveAsync(setKey, newKey);
                await cache.RemoveAsync(newKey, ct);
                await db.SetRemoveAsync(setKey, Key(current));
                await cache.RemoveAsync(Key(current), ct);
                return null;
            }

            return new SessionRotation(newId, now + slidingTtl);
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

    // Single-winner rotation claim (SET NX). Manually prefixed since it goes via
    // IConnectionMultiplexer, not IDistributedCache. The value is an unused sentinel.
    private const string RotationClaimValue = "1";
    private static string RotationClaimKey(SessionId sessionId) => $"{KeyPrefix}{Key(sessionId)}:rotating";

    // COND-B revocation tombstone (SET EX). Set by InvalidateAllForUserAsync before it
    // snapshots the index; RotateAsync fails closed while it exists. Manually prefixed
    // (IConnectionMultiplexer path). The value is an unused sentinel.
    private const string RevokedValue = "1";
    private static string UserRevokedKey(Guid userId) =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}user:{userId}:revoked");

    // RotatedAt + Lifetime are written from PR2a. Older payloads lack them: Lifetime
    // deserializes to Legacy (ordinal 0) and RotatedAt to default — both handled at the
    // read site (profile selection). SupersededAt (2b-3) marks a key that has been rotated
    // away and is living out its fixed grace window (COND-A); a missing JSON property
    // deserializes to null (no grace in flight), so pre-field payloads stay valid.
    private sealed record SessionPayload(
        Guid UserId,
        DateTimeOffset CreatedAt,
        DateTimeOffset RotatedAt,
        SessionLifetime Lifetime,
        DateTimeOffset? SupersededAt = null);
}
