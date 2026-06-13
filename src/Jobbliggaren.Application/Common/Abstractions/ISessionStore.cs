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

public interface ISessionStore
{
    Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct);

    Task<Session> CreateAsync(Guid userId, CancellationToken ct);

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
