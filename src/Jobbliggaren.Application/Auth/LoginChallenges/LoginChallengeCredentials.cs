using System.Buffers.Text;
using System.Security.Cryptography;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

// The three credentials of a login challenge share SessionId's shape (ISessionStore.cs): the value is private,
// Reveal() hands it out on purpose, and ToString() prints a six-character prefix — so an interpolated log line
// or a failed assertion never prints a live credential in full.

/// <summary>A login challenge's id: 128 bits from the CSPRNG, Base64Url. Handed to the requester.</summary>
public readonly record struct ChallengeId
{
    /// <summary>Bytes behind the id — the link token carries them next to its secret.</summary>
    public const int ByteLength = 16;

    private readonly string _value = string.Empty;

    private ChallengeId(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => _value is { Length: >= 6 } ? $"{_value[..6]}…" : "…";

    public static ChallengeId Generate() =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    public static ChallengeId FromRaw(string raw) => new(raw);
}

/// <summary>A six-digit login code, as minted by the store or as presented by a client.</summary>
public readonly record struct LoginCode
{
    private readonly string _value = string.Empty;

    private LoginCode(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => "…";

    public static LoginCode FromRaw(string raw) => new(raw);
}

/// <summary>
/// A magic-link token: the challenge id's bytes and a 128-bit secret, Base64Url, carried as the link's one
/// <c>token</c> parameter (ADR 0142 "Page form").
/// </summary>
public readonly record struct LoginLinkToken
{
    private readonly string _value = string.Empty;

    private LoginLinkToken(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => _value is { Length: >= 6 } ? $"{_value[..6]}…" : "…";

    public static LoginLinkToken FromRaw(string raw) => new(raw);
}
