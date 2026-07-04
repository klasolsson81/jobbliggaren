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
    DateTimeOffset ExpiresAt);

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

public interface ISessionStore
{
    Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct);

    // Creates a session under the given lifetime profile (#481 persistent-login).
    // Session-id rotation + the /auth/refresh seam that drives it land in the follow-up
    // PR (2b) together with the "Håll mig inloggad" checkbox that first produces a
    // rotating Persistent session — until then every session is Legacy (no rotation),
    // so rotation is dormant and is designed there against the real RedisCache format.
    Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct);

    Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct);

    /// <summary>
    /// Invaliderar alla aktiva sessioner för en användare. Anropas vid
    /// kontoradering (DELETE /me) post-commit per ADR 0024 D4 + ADR 0017
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
}
