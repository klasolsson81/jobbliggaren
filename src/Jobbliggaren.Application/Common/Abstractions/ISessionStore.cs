using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Common.Abstractions;

[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    private readonly string _value = string.Empty;

    private SessionId(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() =>
        _value is { Length: >= 6 } ? $"{_value[..6]}…" : "…";

    public static SessionId Generate()
    {
        const int ByteLength = 32;
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return new SessionId(
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    public static SessionId FromRaw(string raw) => new(raw);
}

internal sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString()
            ?? throw new JsonException("SessionId kan inte vara null.");
        return SessionId.FromRaw(raw);
    }

    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Reveal());
}

public sealed record Session(
    SessionId Id,
    Guid UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    // The lifetime profile the session was created under (#481). Exposed so a caller that
    // re-issues the current session (change-password C6, #678) can mint the replacement under
    // the SAME profile and tell the Next layer whether the cookie is persistent — without a
    // second store round-trip or a new port method. Every store read/create populates this
    // authoritatively from the payload/param. The Legacy default is purely a construction
    // convenience for test doubles that don't assert on it — it is NOT the pre-profiles
    // back-compat mechanism, which lives separately in SessionPayload deserialization (a
    // pre-profiles JSON without a Lifetime field decodes to ordinal 0 = Legacy there).
    SessionLifetime Lifetime = SessionLifetime.Legacy);

/// <summary>
/// Which lifetime profile a session was created under (#481 persistent-login).
/// The value is persisted in the session payload, so a session keeps its profile
/// across reads. <see cref="Legacy"/> is ordinal 0 on purpose: a pre-profiles
/// payload (which has no lifetime field) deserializes to 0 → Legacy → today's
/// reach, so no user is logged out on the deploy that introduces this.
/// </summary>
public enum SessionLifetime
{
    /// <summary>Pre-profiles reach (the value in effect before opt-in persistence).</summary>
    Legacy = 0,

    /// <summary>Short-lived: "Håll mig inloggad" unchecked. Dies quickly, never rotates.</summary>
    Session = 1,

    /// <summary>"Håll mig inloggad" checked: long sliding window, hard cap, rotates on interval.</summary>
    Persistent = 2,
}

/// <summary>Result of a due session-id rotation: the freshly minted id and its expiry.</summary>
public sealed record SessionRotation(SessionId NewId, DateTimeOffset ExpiresAt);

public interface ISessionStore
{
    Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct);

    // Creates a session under the given lifetime profile (#481 persistent-login).
    Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct);

    /// <summary>
    /// Rotates the session id if the session's profile has a rotation interval that has
    /// elapsed since the last rotation, collapsing a captured token's replay window to
    /// that interval (security C3). Preserves the original CreatedAt (so the absolute cap
    /// never resets) and the lifetime profile; the old id is invalidated. Returns null
    /// when rotation is not due, the session is gone, or a concurrent refresh won the
    /// single-winner election. The caller (the /auth/refresh seam) writes the new id into
    /// the cookie — the backend cannot set cookies (ADR 0018). Driven by the Next.js proxy
    /// refresh seam as of the 2b-3b activation (rememberMe -> Persistent + the refresh driver).
    /// </summary>
    Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct);

    Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct);

    /// <summary>
    /// Invaliderar alla aktiva sessioner för en användare. Anropas vid
    /// kontoradering (POST /me/delete) post-commit per ADR 0024 D4 + ADR 0017
    /// "Out of Scope (Deferred)"-sektion (account-deletion-flow).
    ///
    /// Implementation: Redis secondary index `user:{userId}:sessions`
    /// (SET) trackar session-IDs per användare. Vid invalidate-all
    /// itereras setets medlemmar och varje session-key droppas, sedan
    /// setet självt. O(N) över användarens sessioner — typiskt 1-3 i
    /// Fas 1.
    /// </summary>
    /// <returns>Antal sessions som invaliderades.</returns>
    Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Plants a per-user "deleted" tombstone so <see cref="GetAsync"/> fail-closed rejects
    /// EVERY surviving session for the user, closing the read-path erasure gap that the
    /// best-effort <see cref="InvalidateAllForUserAsync"/> leaves if it partially fails (Redis
    /// blip, or a session created between the soft-delete commit and the invalidation). Called
    /// on account deletion alongside <see cref="InvalidateAllForUserAsync"/> — the tombstone is
    /// the durable backstop, the eager invalidation the fast path (GDPR Art. 17 read-path).
    ///
    /// The tombstone lives for the 30-day soft-delete restore window and self-expires, so it
    /// never blocks a later account (post hard-delete the id is gone anyway). A future in-window
    /// restore path MUST clear this tombstone before the account is used again — see
    /// docs/runbooks/account-deletion.md. Layer 2 of the PR2c soft-delete gate; mirrors the
    /// COND-B revocation tombstone. Idempotent.
    /// </summary>
    Task MarkUserDeletedAsync(Guid userId, CancellationToken ct);
}
