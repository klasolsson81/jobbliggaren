using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// The OAuth <c>state</c> (ADR 0142 D8): 256 bits from the CSPRNG, Base64Url. It is the key to the flow's record.
/// <see cref="ToString"/> prints a six-character prefix only, as <c>GrantToken</c> does.
/// </summary>
public readonly record struct OAuthState
{
    public const int ByteLength = 32;

    /// <summary>The length of a minted state: 32 bytes, Base64Url without padding.</summary>
    public const int EncodedLength = 43;

    private readonly string _value;

    private OAuthState(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => _value is { Length: >= 6 } ? $"{_value[..6]}…" : "…";

    public static OAuthState Generate() =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    public static OAuthState FromRaw(string raw) => new(raw);
}

/// <summary>
/// The PKCE <c>code_verifier</c> (RFC 7636 §4.1): 256 bits, Base64Url, 43 characters of the unreserved set. It stays
/// in the flow's protected record and is sent only to the token endpoint. <see cref="ToString"/> prints none of it.
/// </summary>
public readonly record struct PkceVerifier
{
    public const int ByteLength = 32;

    private readonly string _value;

    private PkceVerifier(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => "PkceVerifier(redacted)";

    public static PkceVerifier Generate() =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    public static PkceVerifier FromRaw(string raw) => new(raw);

    /// <summary>The S256 challenge (RFC 7636 §4.2): BASE64URL(SHA256(ASCII(verifier))). The only method used.</summary>
    public PkceChallenge ToChallenge() =>
        PkceChallenge.Derive(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(_value))));
}

/// <summary>An S256 PKCE challenge. Derived from a verifier only; it has no public constructor and no parser.</summary>
public readonly record struct PkceChallenge
{
    private PkceChallenge(string value) => Value = value;

    public string Value { get; }

    public const string Method = "S256";

    internal static PkceChallenge Derive(string value) => new(value);
}

/// <summary>
/// The authorization code a provider hands the browser. Single use and short-lived at the provider, and a
/// credential all the same: <see cref="ToString"/> prints none of it.
/// </summary>
public readonly record struct AuthorizationCode
{
    private readonly string _value;

    private AuthorizationCode(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => "AuthorizationCode(redacted)";

    public static AuthorizationCode FromRaw(string raw) => new(raw);
}
